﻿using RTCV.NetCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RTCV;
using RTCV.CorruptCore;
using RTCV.Common;
using FileStub;

namespace Vanguard
{
    public static class VanguardImplementation
    {
        public static RTCV.Vanguard.VanguardConnector connector = null;


        public static void StartClient()
        {
            try
            {
                ConsoleEx.WriteLine("Starting Vanguard Client");
                Thread.Sleep(500); //When starting in Multiple Startup Project, the first try will be uncessful since
                                   //the server takes a bit more time to start then the client.

                var spec = new NetCoreReceiver();
                spec.Attached = VanguardCore.attached;
                spec.MessageReceived += OnMessageReceived;

                connector = new RTCV.Vanguard.VanguardConnector(spec);
            }
            catch (Exception ex)
            {
                if (VanguardCore.ShowErrorDialog(ex, true) == DialogResult.Abort)
                    throw new RTCV.NetCore.AbortEverythingException();
            }
        }

        public static void RestartClient()
        {
            connector?.Kill();
            connector = null;
            StartClient();
        }

        private static void OnMessageReceived(object sender, NetCoreEventArgs e)
        {
            try
            {
                // This is where you implement interaction.
                // Warning: Any error thrown in here will be caught by NetCore and handled by being displayed in the console.

                var message = e.message;
                var simpleMessage = message as NetCoreSimpleMessage;
                var advancedMessage = message as NetCoreAdvancedMessage;

                ConsoleEx.WriteLine(message.Type);
                switch (message.Type) //Handle received messages here
                {

                    case RTCV.NetCore.Commands.Remote.AllSpecSent:
                        {
                            //We still need to set the emulator's path
                            AllSpec.VanguardSpec.Update(VSPEC.EMUDIR, FileWatch.currentDir);
                            SyncObjectSingleton.FormExecute(() =>
                            {
                                FileWatch.UpdateDomains();
                            });
                        }
                        break;
                    case RTCV.NetCore.Commands.Basic.SaveSavestate:

                        string key = (advancedMessage.objectValue as string);

                        //TODO: Sync states with keys

                        SyncObjectSingleton.FormExecute(() =>
                        {
                            S.GET<StubForm>().btnRamSaveState_Click(null, null);
                            string returnKey = VanguardCore.SaveSavestate_NET(key);
                            e.setReturnValue(returnKey);
                        });

                        break;

                    case RTCV.NetCore.Commands.Basic.LoadSavestate:

                        var cmd = advancedMessage.objectValue as object[];
                        var path = cmd[0] as string;
                        var location = (StashKeySavestateLocation)cmd[1];

                        SyncObjectSingleton.FormExecute(() =>
                        {


                            //e.setReturnValue(VanguardCore.LoadSavestate_NET(path, location));
                            VanguardCore.LoadSavestate_NET(path, location);
                            S.GET<StubForm>().RepackState(false);
                        });

                        e.setReturnValue(true);

                        break;

                    case RTCV.NetCore.Commands.Remote.PreCorruptAction:
                        FileWatch.KillProcess();
                        FileWatch.currentFileInfo.targetInterface.CloseStream();
                        FileWatch.RestoreTarget();
                        break;

                    case RTCV.NetCore.Commands.Remote.PostCorruptAction:
                        //var fileName = advancedMessage.objectValue as String;
                        FileWatch.currentFileInfo.targetInterface?.CloseStream();
                        SyncObjectSingleton.FormExecute(() =>
                        {
                            Executor.Execute();
                        });
                        break;

                    case RTCV.NetCore.Commands.Remote.CloseGame:
                        SyncObjectSingleton.FormExecute(() =>
                        {
                            FileWatch.KillProcess();
                        });
                        break;

                    case RTCV.NetCore.Commands.Remote.DomainGetDomains:
                        SyncObjectSingleton.FormExecute(() =>
                        {
                            e.setReturnValue(FileWatch.GetInterfaces());
                        });
                        break;

                    case RTCV.NetCore.Commands.Remote.EventEmuManiformClose:
                        SyncObjectSingleton.FormExecute(() =>
                        {
                            Environment.Exit(0);
                        });
                        break;
                    case RTCV.NetCore.Commands.Remote.IsNormalAdvance:
                        e.setReturnValue(true);
                        break;

                    case RTCV.NetCore.Commands.Remote.EventCloseEmulator:
                        Environment.Exit(-1);
                        break;
                }
            }
            catch (Exception ex)
            {
                if (VanguardCore.ShowErrorDialog(ex, true) == DialogResult.Abort)
                    throw new RTCV.NetCore.AbortEverythingException();
            }
        }

    }
}
