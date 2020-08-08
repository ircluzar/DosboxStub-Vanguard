﻿using Newtonsoft.Json;
using RTCV.CorruptCore;
using RTCV.Common;
using RTCV.NetCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Vanguard;
using FileStub;

namespace FileStub
{
    public static class FileWatch
    {
        public static string FileStubVersion = "0.0.3";
        public static string currentDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        public static string DosboxSavestateFolder = Path.Combine(FileWatch.currentDir, "DOSBOX", "save");
        public static string DosboxSavestateWorkFolder = Path.Combine(FileWatch.currentDir, "DOSBOX", "save", "1");
        public static string DosboxSavestateWorkMemoryFile = Path.Combine(FileWatch.currentDir, "DOSBOX", "save", "1", "Memory");

        public static FileStubFileInfo currentFileInfo = new FileStubFileInfo();

        public static ProgressForm progressForm;

        const int UNCOMPRESSED_MEMORY_OFFSET = 0x7F;



        public static void Start()
        {
            RTCV.Common.Logging.StartLogging(VanguardCore.logPath);
            if (VanguardCore.vanguardConnected)
                RemoveDomains();

            //FileWatch.currentFileInfo = new FileStubFileInfo();

            DisableInterface();
            //state = TargetType.UNFOUND;

            RtcCore.EmuDirOverride = true; //allows the use of this value before vanguard is connected


            string backupPath = Path.Combine(FileWatch.currentDir, "FILEBACKUPS");
            string paramsPath = Path.Combine(FileWatch.currentDir, "PARAMS");

            if (!Directory.Exists(backupPath))
                Directory.CreateDirectory(backupPath);

            if (!Directory.Exists(paramsPath))
                Directory.CreateDirectory(paramsPath);

            string disclaimerPath = Path.Combine(currentDir, "LICENSES", "DISCLAIMER.TXT");
            string disclaimerReadPath = Path.Combine(currentDir, "PARAMS", "DISCLAIMERREAD");

            if (File.Exists(disclaimerPath) && !File.Exists(disclaimerReadPath))
            {
                MessageBox.Show(File.ReadAllText(disclaimerPath).Replace("[ver]", FileWatch.FileStubVersion), "File Stub", MessageBoxButtons.OK, MessageBoxIcon.Information);
                File.Create(disclaimerReadPath);
            }

            //If we can't load the dictionary, quit the wgh to prevent the loss of backups
            if (!FileInterface.LoadCompositeFilenameDico(FileWatch.currentDir))
                Application.Exit();

        }

        private static void RemoveDomains()
        {
            if(currentFileInfo.targetInterface != null)
            {
                currentFileInfo.targetInterface.CloseStream();
                currentFileInfo.targetInterface = null;
            }

            UpdateDomains();
        }


        public static bool RestoreTarget()
        {
            bool success = false;
            if (currentFileInfo.autoUncorrupt)
            {
                if (StockpileManager_EmuSide.UnCorruptBL != null)
                {
                    StockpileManager_EmuSide.UnCorruptBL.Apply(false);
                    success = true;
                }
                else
                {
                    //CHECK CRC WITH BACKUP HERE AND SKIP BACKUP IF WORKING FILE = BACKUP FILE
                   success = currentFileInfo.targetInterface.ResetWorkingFile();
                }
            }
            else
            {
                success = currentFileInfo.targetInterface.ResetWorkingFile();
            }
            return success;
        }

        internal static bool LoadTarget()
        {

            FileInterface.identity = FileInterfaceIdentity.SELF_DESCRIBE;

            

            if (!File.Exists(DosboxSavestateWorkMemoryFile))
            {
                MessageBox.Show($"Could not find part of the path {DosboxSavestateWorkMemoryFile}\n\nMake sure you have created your savestate with the button in this interface\n\nIf you changed the Save State slot, put it back to 1.");
                return false;
            }


            string targetId = "File|" + DosboxSavestateWorkMemoryFile;

            CloseTarget(false);


            FileInterface fi = null;

            

            Action<object, EventArgs> action = (ob, ea) =>
            {
                fi = new FileInterface(targetId, FileWatch.currentFileInfo.bigEndian, true, _startPadding: UNCOMPRESSED_MEMORY_OFFSET);

                if (FileWatch.currentFileInfo.useCacheAndMultithread)
                    fi.getMemoryDump();
            };

            Action<object, EventArgs> postAction = (ob, ea) =>
            {
                if (fi == null || fi.lastMemorySize == null)
                {
                    MessageBox.Show("Failed to load target");
                    S.GET<StubForm>().DisableTargetInterface();
                    return;
                }

                FileWatch.currentFileInfo.targetShortName = fi.ShortFilename;
                FileWatch.currentFileInfo.targetFullName = fi.Filename;

                FileWatch.currentFileInfo.targetInterface = fi;
                S.GET<StubForm>().lbTarget.Text = targetId + "|MemorySize:" + fi.lastMemorySize.ToString();

                if (VanguardCore.vanguardConnected)
                    UpdateDomains();

                //Refresh the UI
                //RefreshUIPostLoad();
            };

            S.GET<StubForm>().RunProgressBar($"Loading target...", 0, action, postAction);


            return true;
        }

        internal static void KillProcess()
        {
            if (currentFileInfo.selectedExecution == ExecutionType.EXECUTE_OTHER_PROGRAM || 
                currentFileInfo.selectedExecution == ExecutionType.EXECUTE_WITH || 
                currentFileInfo.selectedExecution == ExecutionType.EXECUTE_CORRUPTED_FILE)
                if (currentFileInfo.TerminateBeforeExecution && Executor.otherProgram != null)
                {

                    string otherProgramShortFilename = Path.GetFileName(Executor.otherProgram);

                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = "taskkill";
                    startInfo.Arguments = $"/IM \"{otherProgramShortFilename}\"";
                    startInfo.RedirectStandardOutput = true;
                    startInfo.RedirectStandardError = true;
                    startInfo.UseShellExecute = false;
                    startInfo.CreateNoWindow = true;

                    Process processTemp = new Process();
                    processTemp.StartInfo = startInfo;
                    processTemp.EnableRaisingEvents = true;
                    try
                    {
                        processTemp.Start();
                        processTemp.WaitForExit();
                        Thread.Sleep(500); //Add an artificial delay as sometimes handles don't release right away 
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }

                    //Thread.Sleep(300);
                }
        }

        internal static bool CloseTarget(bool updateDomains = true)
        {
            if (FileWatch.currentFileInfo.targetInterface != null)
            {
                if (!FileWatch.RestoreTarget())
                {
                    MessageBox.Show("Unable to restore the backup. Aborting!");
                    return false;
                }
                FileWatch.currentFileInfo.targetInterface.CloseStream();
                FileWatch.currentFileInfo.targetInterface = null;
            }

            if(updateDomains)
                UpdateDomains();
            return true;
        }

        public static void UpdateDomains()
        {
            try
            {


                PartialSpec gameDone = new PartialSpec("VanguardSpec");
                gameDone[VSPEC.SYSTEM] = "Dosbox-x";
                gameDone[VSPEC.GAMENAME] = FileWatch.currentFileInfo.targetShortName;
                gameDone[VSPEC.SYSTEMPREFIX] = "Dosbox";
                gameDone[VSPEC.SYSTEMCORE] = "Dosbox";
                //gameDone[VSPEC.SYNCSETTINGS] = BIZHAWK_GETSET_SYNCSETTINGS;
                gameDone[VSPEC.OPENROMFILENAME] = currentFileInfo.targetFullName;
                gameDone[VSPEC.MEMORYDOMAINS_BLACKLISTEDDOMAINS] = new string[0];
                gameDone[VSPEC.MEMORYDOMAINS_INTERFACES] = GetInterfaces();
                gameDone[VSPEC.CORE_DISKBASED] = false;
                AllSpec.VanguardSpec.Update(gameDone);

                //This is local. If the domains changed it propgates over netcore
                LocalNetCoreRouter.Route(NetcoreCommands.CORRUPTCORE, NetcoreCommands.REMOTE_EVENT_DOMAINSUPDATED, true, true);

                //Asks RTC to restrict any features unsupported by the stub
                LocalNetCoreRouter.Route(NetcoreCommands.CORRUPTCORE, NetcoreCommands.REMOTE_EVENT_RESTRICTFEATURES, true, true);

            }
            catch (Exception ex)
            {
                if (VanguardCore.ShowErrorDialog(ex) == DialogResult.Abort)
                    throw new RTCV.NetCore.AbortEverythingException();
            }
        }

        public static MemoryDomainProxy[] GetInterfaces()
        {
            try
            {
                Console.WriteLine($" getInterfaces()");
                if (currentFileInfo.targetInterface == null)
                {
                    Console.WriteLine($"rpxInterface was null!");
                    return new MemoryDomainProxy[] { };
                }

                List<MemoryDomainProxy> interfaces = new List<MemoryDomainProxy>();

                interfaces.Add(new MemoryDomainProxy(currentFileInfo.targetInterface));

                //switch (currentFileInfo.selectedTargetType)
                //{   //Checking if the FileInterface/MultiFileInterface is split in sub FileInterfaces 

                //    case TargetType.MULTIPLE_FILE_MULTIDOMAIN:
                //    case TargetType.MULTIPLE_FILE_MULTIDOMAIN_FULLPATH:
                //        foreach (var fi in (currentFileInfo.targetInterface as MultipleFileInterface).FileInterfaces)
                //            interfaces.Add(new MemoryDomainProxy(fi));
                //        break;
                //    case TargetType.SINGLE_FILE:
                //    case TargetType.MULTIPLE_FILE_SINGLEDOMAIN:
                //    default:
                //        interfaces.Add(new MemoryDomainProxy(currentFileInfo.targetInterface));
                //        break;
                //}

                foreach (MemoryDomainProxy mdp in interfaces)
                    mdp.BigEndian = currentFileInfo.bigEndian;

                return interfaces.ToArray();
            }
            catch (Exception ex)
            {
                if (VanguardCore.ShowErrorDialog(ex, true) == DialogResult.Abort)
                    throw new RTCV.NetCore.AbortEverythingException();

                return new MemoryDomainProxy[] { };
            }

        }

        public static void EnableInterface()
        {
            S.GET<StubForm>().btnResetBackup.Enabled = true;
            S.GET<StubForm>().btnRestoreBackup.Enabled = true;
        }

        public static void DisableInterface()
        {
            S.GET<StubForm>().btnResetBackup.Enabled = false;
            S.GET<StubForm>().btnRestoreBackup.Enabled = false;
        }

    }


}
