using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System.IO;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Timers;
using System.Diagnostics;
using MSIAfterburnerNET.HM.Interop;
using CommonPluginsShared;
using System.Windows;
using Playnite.SDK.Events;
using GameActivity.Controls;
using GameActivity.Models;
using GameActivity.Services;
using GameActivity.Views;
using System.Threading.Tasks;
using CommonPluginsShared.PlayniteExtended;
using System.Windows.Media;
using CommonPluginsShared.Controls;
using CommonPlayniteShared.Common;
using CommonPluginsShared.Extensions;
using System.Threading;
using QuickSearch.SearchItems;
using MoreLinq;
using CommonPluginsControls.Views;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;

namespace GameActivity
{
    public class GameActivity : PluginExtended<GameActivitySettingsViewModel, ActivityDatabase>
    {
        public override Guid Id { get; } = Guid.Parse("afbb1a0d-04a1-4d0c-9afa-c6e42ca855b4");

        internal TopPanelItem topPanelItem;
        internal GameActivityViewSidebar gameActivityViewSidebar;

        private List<RunningActivity> runningActivities = new List<RunningActivity>();

        private GameActivities gameActivities;

        //private OldToNew oldToNew;


        public GameActivity(IPlayniteAPI api) : base(api)
        {
            // Old database            
            //oldToNew = new OldToNew(this.GetPluginUserDataPath());

            // Custom theme button
            EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler(OnCustomThemeButtonClick));

            // Custom elements integration
            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                ElementList = new List<string> { "PluginButton", "PluginChartTime", "PluginChartLog" },
                SourceName = "GameActivity"
            });

            // Settings integration
            AddSettingsSupport(new AddSettingsSupportArgs
            {
                SourceName = "GameActivity",
                SettingsRoot = $"{nameof(PluginSettings)}.{nameof(PluginSettings.Settings)}"
            });

            // Remove duplicate
            Task.Run(() =>
            {
                if (!PluginSettings.Settings.HasRemovingDuplicate)
                {
                    System.Threading.SpinWait.SpinUntil(() => PluginDatabase.IsLoaded, -1);

                    GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(
                        $"GameActivity - Database updating...",
                        false
                    );
                    globalProgressOptions.IsIndeterminate = false;

                    PlayniteApi.Dialogs.ActivateGlobalProgress((activateGlobalProgress) =>
                    {
                        activateGlobalProgress.ProgressMaxValue = PluginDatabase.Database.Count;

                        foreach (GameActivities gameActivities in PluginDatabase.Database)
                        {
                            Thread.Sleep(10);
                            try
                            {
                                double countBefore = gameActivities.Items.Count();
                                gameActivities.Items = gameActivities.Items.DistinctBy(x => new { x.DateSession, x.ElapsedSeconds }).ToList();
                                double countAfter = gameActivities.Items.Count();

                                if (countBefore > countAfter)
                                {
                                    logger.Warn($"Duplicate items ({countBefore - countAfter}) in {gameActivities.Name}");
                                    PluginDatabase.Update(gameActivities);
                                }
                            }
                            catch (Exception ex)
                            {
                                Common.LogError(ex, false, true, PluginDatabase.PluginName);
                            }

                            activateGlobalProgress.CurrentProgressValue++;
                        }

                        PluginSettings.Settings.HasRemovingDuplicate = true;
                        Application.Current.Dispatcher.BeginInvoke((Action)delegate
                        {
                            this.SavePluginSettings(PluginSettings.Settings);
                        });
                    }, globalProgressOptions);
                }
            });

            // Initialize top & side bar
            if (API.Instance.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                topPanelItem = new TopPanelItem()
                {
                    Icon = new TextBlock
                    {
                        Text = "\ue97f",
                        FontSize = 20,
                        FontFamily = resources.GetResource("FontIcoFont") as FontFamily
                    },
                    Title = resources.GetString("LOCGameActivityViewGamesActivities"),
                    Activated = () =>
                    {
                        var windowOptions = new WindowOptions
                        {
                            ShowMinimizeButton = false,
                            ShowMaximizeButton = true,
                            ShowCloseButton = true,
                            Width = 1280,
                            Height = 740
                        };

                        var ViewExtension = new GameActivityView();
                        Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(PlayniteApi, resources.GetString("LOCGamesActivitiesTitle"), ViewExtension, windowOptions);
                        windowExtension.ResizeMode = ResizeMode.CanResize;
                        windowExtension.ShowDialog();
                    },
                    Visible = PluginSettings.Settings.EnableIntegrationButtonHeader
                };

                gameActivityViewSidebar = new GameActivityViewSidebar(this);
            }
        }


        #region Custom event
        public void OnCustomThemeButtonClick(object sender, RoutedEventArgs e)
        {
            string ButtonName = string.Empty;
            try
            {
                ButtonName = ((Button)sender).Name;
                if (ButtonName == "PART_CustomGameActivityButton")
                {
                    Common.LogDebug(true, $"OnCustomThemeButtonClick()");

                    var windowOptions = new WindowOptions
                    {
                        ShowMinimizeButton = false,
                        ShowMaximizeButton = true,
                        ShowCloseButton = true,
                        Width = 1280,
                        Height = 740
                    };

                    var ViewExtension = new GameActivityViewSingle(PluginDatabase.GameContext);
                    Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(PlayniteApi, resources.GetString("LOCGameActivity"), ViewExtension, windowOptions);
                    windowExtension.ResizeMode = ResizeMode.CanResize;
                    windowExtension.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }
        }

        private bool CheckGoodForLogging(bool WithNotification = false)
        {
            if (PluginSettings.Settings.EnableLogging && PluginSettings.Settings.UseHWiNFO)
            {
                bool runHWiNFO = false;
                Process[] pname = Process.GetProcessesByName("HWiNFO32");
                if (pname.Length != 0)
                {
                    runHWiNFO = true;
                }
                else
                {
                    pname = Process.GetProcessesByName("HWiNFO64");
                    if (pname.Length != 0)
                    {
                        runHWiNFO = true;
                    }
                }

                if (!runHWiNFO && WithNotification)
                {
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        $"{PluginDatabase.PluginName}-runHWiNFO",
                        PluginDatabase.PluginName + Environment.NewLine + resources.GetString("LOCGameActivityNotificationHWiNFO"),
                        NotificationType.Error,
                        () => OpenSettingsView()
                    ));
                }

                if (!runHWiNFO)
                {
                    logger.Error("No HWiNFO running");
                }

                if (!WithNotification)
                {
                    return runHWiNFO;
                }
            }

            if (PluginSettings.Settings.EnableLogging && PluginSettings.Settings.UseMsiAfterburner)
            {
                bool runMSI = false;
                bool runRTSS = false;
                Process[] pname = Process.GetProcessesByName("MSIAfterburner");
                if (pname.Length != 0)
                {
                    runMSI = true;
                }
                pname = Process.GetProcessesByName("RTSS");
                if (pname.Length != 0)
                {
                    runRTSS = true;
                }

                if ((!runMSI || !runRTSS) && WithNotification)
                {
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        $"{PluginDatabase.PluginName }- runMSI",
                        PluginDatabase.PluginName + Environment.NewLine + resources.GetString("LOCGameActivityNotificationMSIAfterBurner"),
                        NotificationType.Error,
                        () => OpenSettingsView()
                    ));
                }

                if (!runMSI)
                {
                    logger.Warn("No MSI Afterburner running");
                }
                if (!runRTSS)
                {
                    logger.Warn("No RivaTunerStatisticsServer running");
                }

                if (!WithNotification)
                {
                    if ((!runMSI || !runRTSS))
                    {
                        return false;
                    }
                    return true;
                }
            }

            return false;
        }


        #region Timer functions
        /// <summary>
        /// Start the timer.
        /// </summary>
        public void DataLogging_start(Guid Id)
        {
            logger.Info($"DataLogging_start - {Id}");
            
            RunningActivity runningActivity = runningActivities.Find(x => x.Id == Id);

            runningActivity.timer = new System.Timers.Timer(PluginSettings.Settings.TimeIntervalLogging * 60000);
            runningActivity.timer.AutoReset = true;
            runningActivity.timer.Elapsed += (sender, e) => OnTimedEvent(sender, e, Id);
            runningActivity.timer.Start();
        }

        /// <summary>
        /// Stop the timer.
        /// </summary>
        public void DataLogging_stop(Guid Id)
        {
            logger.Info($"DataLogging_stop - {Id}");

            RunningActivity runningActivity = runningActivities.Find(x => x.Id == Id);
            if (runningActivity.WarningsMessage.Count != 0 && PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                try
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)delegate
                    {
                        var ViewExtension = new WarningsDialogs(runningActivity.WarningsMessage);
                        Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(PlayniteApi, resources.GetString("LOCGameActivityWarningCaption"), ViewExtension);
                        windowExtension.ShowDialog();
                    });
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, $"Error on show WarningsMessage - {Id}", true, PluginDatabase.PluginName);
                }
            }

            runningActivity.timer.AutoReset = false;
            runningActivity.timer.Stop();
        }

        /// <summary>
        /// Event excuted with the timer.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private async void OnTimedEvent(Object source, ElapsedEventArgs e, Guid Id)
        {
            int fpsValue = 0;
            int cpuValue = PerfCounter.GetCpuPercentage();
            int gpuValue = PerfCounter.GetGpuPercentage();
            int ramValue = PerfCounter.GetRamPercentage();
            int gpuTValue = PerfCounter.GetGpuTemperature();
            int cpuTValue = PerfCounter.GetCpuTemperature();
            int cpuPValue = 0;
            int gpuPValue = 0;


            if (PluginSettings.Settings.UseMsiAfterburner && CheckGoodForLogging())
            {
                MSIAfterburnerNET.HM.HardwareMonitor MSIAfterburner = null;

                try
                {
                    MSIAfterburner = new MSIAfterburnerNET.HM.HardwareMonitor();
                }
                catch (Exception ex)
                {
                    logger.Warn("Fail initialize MSIAfterburnerNET");
                    Common.LogError(ex, true, "Fail initialize MSIAfterburnerNET");
                    MSIAfterburner = null;
                }

                if (MSIAfterburner != null)
                {
                    try
                    {
                        cpuPValue = (int)MSIAfterburner.GetEntry(MONITORING_SOURCE_ID.CPU_POWER).Data;
                    }
                    catch (Exception ex)
                    {
                        logger.Warn("Fail get cpuPower");
                        Common.LogError(ex, true, "Fail get cpuPower");
                    }

                    try
                    {
                        gpuPValue = (int)MSIAfterburner.GetEntry(MONITORING_SOURCE_ID.GPU_POWER).Data;
                    }
                    catch (Exception ex)
                    {
                        logger.Warn("Fail get gpuPower");
                        Common.LogError(ex, true, "Fail get gpuPower");
                    }

                    try
                    {
                        fpsValue = (int)MSIAfterburner.GetEntry(MONITORING_SOURCE_ID.FRAMERATE).Data;
                    }
                    catch (Exception ex)
                    {
                        logger.Warn("Fail get fpsValue");
                        Common.LogError(ex, true, "Fail get fpsValue");
                    }

                    try
                    {
                        if (gpuValue == 0)
                        {
                            gpuValue = (int)MSIAfterburner.GetEntry(MONITORING_SOURCE_ID.GPU_USAGE).Data;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn("Fail get gpuValue");
                        Common.LogError(ex, true, "Fail get gpuValue");
                    }

                    try
                    {
                        if (gpuTValue == 0)
                        {
                            gpuTValue = (int)MSIAfterburner.GetEntry(MONITORING_SOURCE_ID.GPU_TEMPERATURE).Data;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn("Fail get gpuTValue");
                        Common.LogError(ex, true, "Fail get gpuTValue");
                    }

                    try
                    {
                        if (cpuTValue == 0)
                        {
                            cpuTValue = (int)MSIAfterburner.GetEntry(MONITORING_SOURCE_ID.CPU_TEMPERATURE).Data;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn("Fail get cpuTValue");
                        Common.LogError(ex, true, "Fail get cpuTValue");
                    }
                }
            }
            else if (PluginSettings.Settings.UseHWiNFO && CheckGoodForLogging())
            {
                HWiNFODumper HWinFO = null;
                List<HWiNFODumper.JsonObj> dataHWinfo = null;

                try
                {
                    HWinFO = new HWiNFODumper();
                    dataHWinfo = HWinFO.ReadMem();
                }
                catch (Exception ex)
                {
                    logger.Error("Fail initialize HWiNFODumper");
                    Common.LogError(ex, true, "Fail initialize HWiNFODumper");
                }

                if (HWinFO != null && dataHWinfo != null)
                {
                    try
                    {
                        foreach (var sensorItems in dataHWinfo)
                        {
                            dynamic sensorItemsOBJ = Serialization.FromJson<dynamic>(Serialization.ToJson(sensorItems));

                            string sensorsID = "0x" + ((uint)sensorItemsOBJ["szSensorSensorID"]).ToString("X");

                            // Find sensors fps
                            if (sensorsID.ToLower() == PluginSettings.Settings.HWiNFO_fps_sensorsID.ToLower())
                            {
                                // Find data fps
                                foreach (var items in sensorItemsOBJ["sensors"])
                                {
                                    dynamic itemOBJ = Serialization.FromJson<dynamic>(Serialization.ToJson(items));
                                    string dataID = "0x" + ((uint)itemOBJ["dwSensorID"]).ToString("X");

                                    if (dataID.ToLower() == PluginSettings.Settings.HWiNFO_fps_elementID.ToLower())
                                    {
                                        fpsValue = (int)Math.Round((Double)itemOBJ["Value"]);
                                    }
                                }
                            }

                            // Find sensors gpu usage
                            if (sensorsID.ToLower() == PluginSettings.Settings.HWiNFO_gpu_sensorsID.ToLower())
                            {
                                // Find data gpu
                                foreach (var items in sensorItemsOBJ["sensors"])
                                {
                                    dynamic itemOBJ = Serialization.FromJson<dynamic>(Serialization.ToJson(items));
                                    string dataID = "0x" + ((uint)itemOBJ["dwSensorID"]).ToString("X");

                                    if (dataID.ToLower() == PluginSettings.Settings.HWiNFO_gpu_elementID.ToLower())
                                    {
                                        gpuValue = (int)Math.Round((Double)itemOBJ["Value"]);
                                    }
                                }
                            }

                            // Find sensors gpu temp
                            if (sensorsID.ToLower() == PluginSettings.Settings.HWiNFO_gpuT_sensorsID.ToLower())
                            {
                                // Find data gpu
                                foreach (var items in sensorItemsOBJ["sensors"])
                                {
                                    dynamic itemOBJ = Serialization.FromJson<dynamic>(Serialization.ToJson(items));
                                    string dataID = "0x" + ((uint)itemOBJ["dwSensorID"]).ToString("X");

                                    if (dataID.ToLower() == PluginSettings.Settings.HWiNFO_gpuT_elementID.ToLower())
                                    {
                                        gpuTValue = (int)Math.Round((Double)itemOBJ["Value"]);
                                    }
                                }
                            }

                            // Find sensors cpu temp
                            if (sensorsID.ToLower() == PluginSettings.Settings.HWiNFO_cpuT_sensorsID.ToLower())
                            {
                                // Find data gpu
                                foreach (var items in sensorItemsOBJ["sensors"])
                                {
                                    dynamic itemOBJ = Serialization.FromJson<dynamic>(Serialization.ToJson(items));
                                    string dataID = "0x" + ((uint)itemOBJ["dwSensorID"]).ToString("X");

                                    if (dataID.ToLower() == PluginSettings.Settings.HWiNFO_cpuT_elementID.ToLower())
                                    {
                                        cpuTValue = (int)Math.Round((Double)itemOBJ["Value"]);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn("Fail get HWiNFO");
                        Common.LogError(ex, true, "Fail get HWiNFO");
                    }
                }
            }


            RunningActivity runningActivity = runningActivities.Find(x => x.Id == Id);
            if (runningActivity == null)
            {
                return;
            }

            // Listing warnings
            bool WarningMinFps = false;
            bool WarningMaxCpuTemp = false;
            bool WarningMaxGpuTemp = false;
            bool WarningMaxCpuUsage = false;
            bool WarningMaxGpuUsage = false;
            bool WarningMaxRamUsage = false;

            if (PluginSettings.Settings.EnableWarning)
            {
                if (PluginSettings.Settings.MinFps != 0 && PluginSettings.Settings.MinFps >= fpsValue)
                {
                    WarningMinFps = true;
                }
                if (PluginSettings.Settings.MaxCpuTemp != 0 && PluginSettings.Settings.MaxCpuTemp <= cpuTValue)
                {
                    WarningMaxCpuTemp = true;
                }
                if (PluginSettings.Settings.MaxGpuTemp != 0 && PluginSettings.Settings.MaxGpuTemp <= gpuTValue)
                {
                    WarningMaxGpuTemp = true;
                }
                if (PluginSettings.Settings.MaxCpuUsage != 0 && PluginSettings.Settings.MaxCpuUsage <= cpuValue)
                {
                    WarningMaxCpuUsage = true;
                }
                if (PluginSettings.Settings.MaxGpuUsage != 0 && PluginSettings.Settings.MaxGpuUsage <= gpuValue)
                {
                    WarningMaxGpuUsage = true;
                }
                if (PluginSettings.Settings.MaxRamUsage != 0 && PluginSettings.Settings.MaxRamUsage <= ramValue)
                {
                    WarningMaxRamUsage = true;
                }

                WarningData Message = new WarningData
                {
                    At = resources.GetString("LOCGameActivityWarningAt") + " " + DateTime.Now.ToString("HH:mm"),
                    FpsData = new Data { Name = resources.GetString("LOCGameActivityFps"), Value = fpsValue, IsWarm = WarningMinFps },
                    CpuTempData = new Data { Name = resources.GetString("LOCGameActivityCpuTemp"), Value = cpuTValue, IsWarm = WarningMaxCpuTemp },
                    GpuTempData = new Data { Name = resources.GetString("LOCGameActivityGpuTemp"), Value = gpuTValue, IsWarm = WarningMaxGpuTemp },
                    CpuUsageData = new Data { Name = resources.GetString("LOCGameActivityCpuUsage"), Value = cpuValue, IsWarm = WarningMaxCpuUsage },
                    GpuUsageData = new Data { Name = resources.GetString("LOCGameActivityGpuUsage"), Value = gpuValue, IsWarm = WarningMaxGpuUsage },
                    RamUsageData = new Data { Name = resources.GetString("LOCGameActivityRamUsage"), Value = ramValue, IsWarm = WarningMaxRamUsage },
                };

                if (WarningMinFps || WarningMaxCpuTemp || WarningMaxGpuTemp || WarningMaxCpuUsage || WarningMaxGpuUsage)
                {
                    runningActivity.WarningsMessage.Add(Message);
                }
            }


            List<ActivityDetailsData> ActivitiesDetailsData = runningActivity.GameActivitiesLog.ItemsDetails.Get(runningActivity.GameActivitiesLog.GetLastSession());
            ActivityDetailsData activityDetailsData = new ActivityDetailsData
            {
                Datelog = DateTime.Now.ToUniversalTime(),
                FPS = fpsValue,
                CPU = cpuValue,
                CPUT = cpuTValue,
                CPUP = cpuPValue,
                GPU = gpuValue,
                GPUT = gpuTValue,
                GPUP = gpuPValue,
                RAM = ramValue
            };
            Common.LogDebug(true, Serialization.ToJson(activityDetailsData));
            ActivitiesDetailsData.Add(activityDetailsData);
        }
        #endregion


        #region Backup functions
        public void DataBackup_start(Guid Id)
        {
            RunningActivity runningActivity = runningActivities.Find(x => x.Id == Id);

            runningActivity.timerBackup = new System.Timers.Timer(60000);
            runningActivity.timerBackup.AutoReset = true;
            runningActivity.timerBackup.Elapsed += (sender, e) => OnTimedBackupEvent(sender, e, Id);
            runningActivity.timerBackup.Start();            
        }

        public void DataBackup_stop(Guid Id)
        {
            RunningActivity runningActivity = runningActivities.Find(x => x.Id == Id);

            runningActivity.timerBackup.AutoReset = false;
            runningActivity.timerBackup.Stop();
        }

        private async void OnTimedBackupEvent(object source, ElapsedEventArgs e, Guid Id)
        {
            try
            {
                RunningActivity runningActivity = runningActivities.Find(x => x.Id == Id);

                ulong ElapsedSeconds = (ulong)(DateTime.Now.ToUniversalTime() - runningActivity.activityBackup.DateSession).TotalSeconds;
                runningActivity.activityBackup.ElapsedSeconds = ElapsedSeconds;
                runningActivity.activityBackup.ItemsDetailsDatas = runningActivity.GameActivitiesLog.ItemsDetails.Get(runningActivity.activityBackup.DateSession);

                string PathFileBackup = Path.Combine(PluginDatabase.Paths.PluginUserDataPath, $"SaveSession_{Id}.json");
                FileSystem.WriteStringToFileSafe(PathFileBackup, Serialization.ToJson(runningActivity.activityBackup));
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }
        }
        #endregion

        #endregion


        #region Theme integration
        // Button on top panel
        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            yield return topPanelItem;
        }

        // List custom controls
        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            if (args.Name == "PluginButton")
            {
                return new PluginButton();
            }

            if (args.Name == "PluginChartTime")
            {
                return new PluginChartTime { DisableAnimations = true, LabelsRotation = true, Truncate = PluginDatabase.PluginSettings.Settings.ChartTimeTruncate };
            }

            if (args.Name == "PluginChartLog")
            {
                return new PluginChartLog { DisableAnimations = true, LabelsRotation = true };
            }
             
            return null;
        }

        // SidebarItem
        public class GameActivityViewSidebar : SidebarItem
        {
            public GameActivityViewSidebar(GameActivity plugin)
            {
                Type = SiderbarItemType.View;
                Title = resources.GetString("LOCGameActivityViewGamesActivities");
                Icon = new TextBlock
                {
                    Text = "\ue97f",
                    FontFamily = resources.GetResource("FontIcoFont") as FontFamily
                };
                Opened = () =>
                {
                    SidebarItemControl sidebarItemControl = new SidebarItemControl(PluginDatabase.PlayniteApi);
                    sidebarItemControl.SetTitle(resources.GetString("LOCGamesActivitiesTitle"));
                    sidebarItemControl.AddContent(new GameActivityView());

                    return sidebarItemControl;
                };
                Visible = plugin.PluginSettings.Settings.EnableIntegrationButtonSide;
            }
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            var items = new List<SidebarItem>
            {
                gameActivityViewSidebar
            };
            return items;
        }
        #endregion


        #region Menus
        // To add new game menu items override GetGameMenuItems
        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            Game GameMenu = args.Games.First();

            List<GameMenuItem> gameMenuItems = new List<GameMenuItem>
            {
                // Show plugin view with all activities for all game in database with data of selected game
                new GameMenuItem {
                    //MenuSection = "",
                    Icon = Path.Combine(PluginFolder, "Resources", "chart-646.png"),
                    Description = resources.GetString("LOCGameActivityViewGameActivity"),
                    Action = (gameMenuItem) =>
                    {
                        var ViewExtension = new GameActivityViewSingle(GameMenu);
                        Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(PlayniteApi, resources.GetString("LOCGameActivity"), ViewExtension);
                        windowExtension.ShowDialog();
                    }
                }
            };

#if DEBUG
            gameMenuItems.Add(new GameMenuItem
            {
                MenuSection = resources.GetString("LOCGameActivity"),
                Description = "Test",
                Action = (mainMenuItem) => 
                {

                }
            });
#endif

            return gameMenuItems;
        }

        // To add new main menu items override GetMainMenuItems
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            string MenuInExtensions = string.Empty;
            if (PluginSettings.Settings.MenuInExtensions)
            {
                MenuInExtensions = "@";
            }

            List<MainMenuItem> mainMenuItems = new List<MainMenuItem>
            {
                // Show plugin view with all activities for all game in database
                new MainMenuItem
                {
                    MenuSection = MenuInExtensions + resources.GetString("LOCGameActivity"),
                    Description = resources.GetString("LOCGameActivityViewGamesActivities"),
                    Action = (mainMenuItem) =>
                    {
                        var windowOptions = new WindowOptions
                        {
                            ShowMinimizeButton = false,
                            ShowMaximizeButton = true,
                            ShowCloseButton = true,
                            Width = 1280,
                            Height = 740
                        };

                        var ViewExtension = new GameActivityView();
                        Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(PlayniteApi, resources.GetString("LOCGamesActivitiesTitle"), ViewExtension, windowOptions);
                        windowExtension.ResizeMode = ResizeMode.CanResize;
                        windowExtension.ShowDialog();
                    }
                },

                new MainMenuItem
                {
                    MenuSection = MenuInExtensions + resources.GetString("LOCGameActivity"),
                    Description = "-"
                },

                // Show plugin view with all activities for all game in database
                new MainMenuItem
                {
                    MenuSection = MenuInExtensions + resources.GetString("LOCGameActivity"),
                    Description = resources.GetString("LOCCommonExportData"),
                    Action = (mainMenuItem) =>
                    {
                        GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(
                            $"GameActivity - {resources.GetString("LOCCommonProcessing")}",
                            false
                        );
                        globalProgressOptions.IsIndeterminate = true;

                        PlayniteApi.Dialogs.ActivateGlobalProgress((activateGlobalProgress) =>
                        {
                            try
                            {
                                List<ExportedData> ExportedDatas = new List<ExportedData>();

                                foreach(GameActivities gameActivities in PluginDatabase.Database)
                                {
                                    List<ExportedData> GameExportedDatas = gameActivities.Items.Select(x => new ExportedData
                                    {
                                        Id = gameActivities.Id,
                                        Name = gameActivities.Name,
                                        LastActivity = gameActivities.LastActivity,

                                        SourceName = x.SourceName,
                                        DateSession = x.DateSession,
                                        ElapsedSeconds = x.ElapsedSeconds
                                    }).ToList();


                                    for(int i = 0; i < GameExportedDatas.Count; i++)
                                    {
                                        List<ActivityDetailsData> ActivityDetailsDatas = gameActivities.GetSessionActivityDetails(GameExportedDatas[i].DateSession);

                                        if (ActivityDetailsDatas.Count > 0)
                                        {
                                            ActivityDetailsDatas.ForEach(x => ExportedDatas.Add(new ExportedData
                                            {
                                                Id = GameExportedDatas[i].Id,
                                                Name = GameExportedDatas[i].Name,
                                                LastActivity = GameExportedDatas[i].LastActivity,

                                                SourceName = GameExportedDatas[i].SourceName,
                                                DateSession = GameExportedDatas[i].DateSession,
                                                ElapsedSeconds = GameExportedDatas[i].ElapsedSeconds,

                                                FPS = x.FPS,
                                                CPU = x.CPU,
                                                GPU = x.GPU,
                                                RAM = x.RAM,
                                                CPUT = x.CPUT,
                                                GPUT = x.GPUT
                                            }));
                                        }
                                        else
                                        {
                                            ExportedDatas.Add(new ExportedData
                                            {
                                                Id = GameExportedDatas[i].Id,
                                                Name = GameExportedDatas[i].Name,
                                                LastActivity = GameExportedDatas[i].LastActivity,

                                                SourceName = GameExportedDatas[i].SourceName,
                                                DateSession = GameExportedDatas[i].DateSession,
                                                ElapsedSeconds = GameExportedDatas[i].ElapsedSeconds
                                            });
                                        }
                                    }
                                }


                                string ExportedDatasCsv = ExportedDatas.ToCsv();
                                string SavPath = PlayniteApi.Dialogs.SaveFile("CSV|*.csv");

                                if (!SavPath.IsNullOrEmpty())
                                {
                                    try
                                    {
                                        FileSystem.WriteStringToFileSafe(SavPath, ExportedDatasCsv);

                                        string Message = string.Format(resources.GetString("LOCCommonExportDataResult"), ExportedDatasCsv.Count());
                                        var result = PlayniteApi.Dialogs.ShowMessage(Message, PluginDatabase.PluginName, MessageBoxButton.YesNo);
                                        if (result == MessageBoxResult.Yes)
                                        {
                                            Process.Start(Path.GetDirectoryName(SavPath));
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Common.LogError(ex, false, true, PluginDatabase.PluginName, PluginDatabase.PluginName + Environment.NewLine + ex.Message);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Common.LogError(ex, true);
                            }

                        }, globalProgressOptions);
                    }
                },


                // Database management
                new MainMenuItem
                {
                    MenuSection = MenuInExtensions + resources.GetString("LOCGameActivity"),
                    Description = "-"
                },

                new MainMenuItem
                {
                    MenuSection = MenuInExtensions + resources.GetString("LOCGameActivity"),
                    Description = resources.GetString("LOCCommonTransferPluginData"),
                    Action = (mainMenuItem) =>
                    {
                        var windowOptions = new WindowOptions
                        {
                            ShowMinimizeButton = false,
                            ShowMaximizeButton = false,
                            ShowCloseButton = true,
                        };

                        var ViewExtension = new TransfertData(PluginDatabase.GetDataGames(), PluginDatabase);
                        Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(PlayniteApi, resources.GetString("LOCCommonSelectTransferData"), ViewExtension, windowOptions);
                        windowExtension.ShowDialog();
                    }
                },

                new MainMenuItem
                {
                    MenuSection = MenuInExtensions + resources.GetString("LOCGameActivity"),
                    Description = resources.GetString("LOCCommonIsolatedPluginData"),
                    Action = (mainMenuItem) =>
                    {
                        var windowOptions = new WindowOptions
                        {
                            ShowMinimizeButton = false,
                            ShowMaximizeButton = false,
                            ShowCloseButton = true,
                        };

                        var ViewExtension = new ListDataWithoutGame(PluginDatabase.GetIsolatedDataGames(), PluginDatabase);
                        Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(PlayniteApi, resources.GetString("LOCCommonIsolatedPluginData"), ViewExtension, windowOptions);
                        windowExtension.ShowDialog();
                    }
                },

                new MainMenuItem
                {
                    MenuSection = MenuInExtensions + resources.GetString("LOCGameActivity"),
                    Description = "Clean Playnite games database",
                    Action = (mainMenuItem) =>
                    {
                        ImportLauhdutinGames("cleanPlayniteLibrary");
                    }
                },

                new MainMenuItem
                {
                    MenuSection = MenuInExtensions + resources.GetString("LOCGameActivity"),
                    Description = "Import Lauhdutin games database",
                    Action = (mainMenuItem) =>
                    {
                        ImportLauhdutinGames("makeTheImport");
                    }
                },

                new MainMenuItem
                {
                    MenuSection = MenuInExtensions + resources.GetString("LOCGameActivity"),
                    Description = "Check Lauhdutin games in Playnite database",
                    Action = (mainMenuItem) =>
                    {
                        ImportLauhdutinGames("checkDifferences");
                    }
                },
            };

#if DEBUG
            mainMenuItems.Add(new MainMenuItem
            {
                MenuSection = MenuInExtensions + resources.GetString("LOCGameActivity"),
                Description = "-"
            });
            mainMenuItems.Add(new MainMenuItem
            {
                MenuSection = MenuInExtensions + resources.GetString("LOCGameActivity"),
                Description = "Test",
                Action = (mainMenuItem) =>
                {

                }
            });
#endif

            return mainMenuItems;
        }
        #endregion


        #region Game event
        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            // Old database
            //if (oldToNew.IsOld)
            //{
            //    oldToNew.ConvertDB(PlayniteApi);
            //}

                       
            // Old format
            //var oldFormat = PluginDatabase.Database?.Select(x => x).Where(x => x.Items.FirstOrDefault() != null && x.Items.FirstOrDefault().PlatformIDs == null);
            var oldFormat = PluginDatabase.Database?.Select(x => x).Where(x => x.Items.Where(y => y.PlatformID != default(Guid)).Count() > 0);
            if (oldFormat?.Count() > 0)
            {
                GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(
                    "GameActivity - Database migration",
                    false
                );
                globalProgressOptions.IsIndeterminate = true;

                PlayniteApi.Dialogs.ActivateGlobalProgress((activateGlobalProgress) =>
                {
                    foreach (GameActivities gameActivities in PluginDatabase.Database)
                    {
                        foreach(Activity activity in gameActivities.Items)
                        {
                            activity.PlatformIDs = new List<Guid> { activity.PlatformID };
                            activity.PlatformID = default(Guid);
                        }

                        PluginDatabase.AddOrUpdate(gameActivities);
                    }
                }, globalProgressOptions);
            }

            try
            {
                if (args.NewValue?.Count == 1 && PluginDatabase.IsLoaded)
                {
                    PluginDatabase.GameContext = args.NewValue[0];
                    PluginDatabase.SetThemesResources(PluginDatabase.GameContext);
                }
                else
                {
                    Task.Run(() =>
                    {
                        System.Threading.SpinWait.SpinUntil(() => PluginDatabase.IsLoaded, -1);

                        Application.Current.Dispatcher.BeginInvoke((Action)delegate
                        {
                            if (args.NewValue?.Count == 1)
                            {
                                PluginDatabase.GameContext = args.NewValue[0];
                                PluginDatabase.SetThemesResources(PluginDatabase.GameContext);
                            }
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }
        }

        // Add code to be executed when game is finished installing.
        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {

        }

        // Add code to be executed when game is uninstalled.
        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {

        }

        // Add code to be executed when game is preparing to be started.
        public override void OnGameStarting(OnGameStartingEventArgs args)
        {

        }

        // Add code to be executed when game is started running.
        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            try
            {
                RunningActivity runningActivity = new RunningActivity();
                runningActivity.Id = args.Game.Id;
                runningActivity.PlaytimeOnStarted = args.Game.Playtime;

                runningActivities.Add(runningActivity);

                DataBackup_start(args.Game.Id);

                // start timer si log is enable.
                if (PluginSettings.Settings.EnableLogging)
                {
                    DataLogging_start(args.Game.Id);
                }

                DateTime DateSession = DateTime.Now.ToUniversalTime();

                runningActivity.GameActivitiesLog = PluginDatabase.Get(args.Game);
                runningActivity.GameActivitiesLog.Items.Add(new Activity
                {
                    IdConfiguration = PluginDatabase?.LocalSystem?.GetIdConfiguration() ?? -1,
                    GameActionName = args.SourceAction?.Name ?? resources.GetString("LOCGameActivityDefaultAction"),
                    DateSession = DateSession,
                    SourceID = args.Game.SourceId == null ? default : args.Game.SourceId,
                    PlatformIDs = args.Game.PlatformIds ?? new List<Guid>()
                });
                runningActivity.GameActivitiesLog.ItemsDetails.Items.TryAdd(DateSession, new List<ActivityDetailsData>());


                runningActivity.activityBackup = new ActivityBackup();
                runningActivity.activityBackup.Id = runningActivity.GameActivitiesLog.Id;
                runningActivity.activityBackup.Name = runningActivity.GameActivitiesLog.Name;
                runningActivity.activityBackup.ElapsedSeconds = 0;
                runningActivity.activityBackup.GameActionName = args.SourceAction?.Name ?? resources.GetString("LOCGameActivityDefaultAction");
                runningActivity.activityBackup.IdConfiguration = PluginDatabase?.LocalSystem?.GetIdConfiguration() ?? -1;
                runningActivity.activityBackup.DateSession = DateSession;
                runningActivity.activityBackup.SourceID = args.Game.SourceId == null ? default : args.Game.SourceId;
                runningActivity.activityBackup.PlatformIDs = args.Game.PlatformIds ?? new List<Guid>();
                runningActivity.activityBackup.ItemsDetailsDatas = new List<ActivityDetailsData>();
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);

                DataBackup_stop(args.Game.Id);
                if (PluginSettings.Settings.EnableLogging)
                {
                    DataLogging_stop(args.Game.Id);
                }
            }
        }

        // Add code to be executed when game is preparing to be started.
        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            var TaskGameStopped = Task.Run(() =>
            {
                try
                {
                    RunningActivity runningActivity = runningActivities.Find(x => x.Id == args.Game.Id);
                    DataBackup_stop(args.Game.Id);

                    // Stop timer si HWiNFO log is enable.
                    if (PluginSettings.Settings.EnableLogging)
                    {
                        DataLogging_stop(args.Game.Id);
                    }

                    ulong ElapsedSeconds = args.ElapsedSeconds;
                    if (ElapsedSeconds == 0)
                    {
                        Thread.Sleep(5000);
                        if (PluginSettings.Settings.SubstPlayStateTime && ExistsPlayStateInfoFile()) // Temporary workaround for PlayState paused time until Playnite allows to share data among extensions
                        {
                            ElapsedSeconds = args.Game.Playtime - runningActivity.PlaytimeOnStarted - GetPlayStatePausedTimeInfo(args.Game);
                        }
                        else
                        {
                            ElapsedSeconds = args.Game.Playtime - runningActivity.PlaytimeOnStarted;
                        }

                        PlayniteApi.Notifications.Add(new NotificationMessage(
                            $"{PluginDatabase.PluginName}- noElapsedSeconds",
                            PluginDatabase.PluginName + System.Environment.NewLine + string.Format(resources.GetString("LOCGameActivityNoPlaytime"), args.Game.Name, ElapsedSeconds),
                            NotificationType.Info
                        ));
                    }
                    else if (PluginSettings.Settings.SubstPlayStateTime && ExistsPlayStateInfoFile()) // Temporary workaround for PlayState paused time until Playnite allows to share data among extensions
                    {
                        Thread.Sleep(10000); // Necessary since PlayState is executed after GameActivity.
                        ElapsedSeconds -= GetPlayStatePausedTimeInfo(args.Game);
                    }

                    // Infos
                    runningActivity.GameActivitiesLog.GetLastSessionActivity(false).ElapsedSeconds = ElapsedSeconds;
                    Common.LogDebug(true, Serialization.ToJson(runningActivity.GameActivitiesLog));
                    PluginDatabase.Update(runningActivity.GameActivitiesLog);

                    if (args.Game.Id == PluginDatabase.GameContext.Id)
                    {
                        PluginDatabase.SetThemesResources(PluginDatabase.GameContext);
                    }

                    // Delete running data
                    runningActivities.Remove(runningActivity);
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, true, PluginDatabase.PluginName);
                }
            });

            // Delete backup
            string PathFileBackup = Path.Combine(PluginDatabase.Paths.PluginUserDataPath, $"SaveSession_{args.Game.Id}.json");
            FileSystem.DeleteFile(PathFileBackup);
        }


        private bool ExistsPlayStateInfoFile() // Temporary workaround for PlayState paused time until Playnite allows to share data among extensions
        {
            // PlayState will write the Id and pausedTime to PlayState.txt file placed inside ExtensionsData Roaming Playnite folder
            // Check first if this file exists and if not return false to avoid executing unnecessary code.
            string PlayStateFile = Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, "PlayState.txt");
            if (File.Exists(PlayStateFile))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private ulong GetPlayStatePausedTimeInfo(Game game) // Temporary workaround for PlayState paused time until Playnite allows to share data among extensions
        {
            // PlayState will write the Id and pausedTime to PlayState.txt file placed inside ExtensionsData Roaming Playnite folder
            // Check first if this file exists and if not return 0 as pausedTime.
            // This check is redundant with ExistsPlayStateInfoFile, but it's because the PlayState file will be modified after the first check, so added as a fallback to avoid exceptions.
            string PlayStateFile = Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, "PlayState.txt");
            if (!File.Exists(PlayStateFile))
            {
                return 0;
            }

            // The file is a simple txt, first line is GameId and second line the paused time.
            string[] PlayStateInfo = File.ReadAllLines(PlayStateFile);
            string Id = PlayStateInfo[0];
            bool isPlayStateInfoCorrect = ulong.TryParse(PlayStateInfo[1], out ulong number);
            ulong PausedSeconds = isPlayStateInfoCorrect ? number : 0;
            if (!isPlayStateInfoCorrect)
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                            $"{PluginDatabase.PluginName}- noPlayStateInfo",
                            PluginDatabase.PluginName + System.Environment.NewLine + $"{game.Name} doesn't have a proper PlayState info file",
                            NotificationType.Info
                        ));
            }

            // After retrieving the info restart the file in order to avoid reusing the same txt if PlayState crash / gets uninstalled.
            string[] Info = { " ", " " };

            File.WriteAllLines(PlayStateFile, Info);

            // Check that the GameId is the same as the paused game. If so, return the paused time. If not, return 0.
            if (game.Id.ToString() == Id)
            {
                return PausedSeconds;
            }
            else
            {
                return 0;
            }
        }
        #endregion


        #region Application event
        // Add code to be executed when Playnite is initialized.
        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // CheckGoodForLogging 
            Task.Run(() =>
            {
                Common.LogDebug(true, "CheckGoodForLogging_1");
                if (!CheckGoodForLogging(false))
                {
                    Thread.Sleep(10000);
                    Common.LogDebug(true, "CheckGoodForLogging_2");
                    if (!CheckGoodForLogging(false))
                    {
                        Thread.Sleep(10000);
                        Common.LogDebug(true, "CheckGoodForLogging_3");
                        if (!CheckGoodForLogging(false))
                        {
                            Thread.Sleep(10000);
                            Common.LogDebug(true, "CheckGoodForLogging_4");
                            Application.Current.Dispatcher.BeginInvoke((Action)delegate
                            {
                                CheckGoodForLogging(true);
                            });
                        }
                    }
                }
            });


            // QuickSearch support
            try
            {
                string icon = Path.Combine(PluginDatabase.Paths.PluginPath, "Resources", "chart-646.png");
                SubItemsAction GaSubItemsAction = new SubItemsAction() { Action = () => { }, Name = "", CloseAfterExecute = false, SubItemSource = new QuickSearchItemSource() };
                CommandItem GaCommand = new CommandItem(PluginDatabase.PluginName, new List<CommandAction>(), ResourceProvider.GetString("LOCGaQuickSearchDescription"), icon);
                GaCommand.Keys.Add(new CommandItemKey() { Key = "ga", Weight = 1 });
                GaCommand.Actions.Add(GaSubItemsAction);
                QuickSearch.QuickSearchSDK.AddCommand(GaCommand);
            }
            catch { }


            // Check backup
            try
            {
                Parallel.ForEach(Directory.EnumerateFiles(PluginDatabase.Paths.PluginUserDataPath, "SaveSession_*.json"), (objectFile) =>
                {
                    Serialization.TryFromJsonFile<ActivityBackup>(objectFile, out ActivityBackup backupData);
                    if (backupData != null)
                    {
                        Application.Current.Dispatcher.BeginInvoke((Action)delegate
                        {
                            PlayniteApi.Notifications.Add(new NotificationMessage(
                                $"{PluginDatabase.PluginName}-backup-{Path.GetFileNameWithoutExtension(objectFile)}",
                                PluginDatabase.PluginName + System.Environment.NewLine + string.Format(resources.GetString("LOCGaBackupExist"), backupData.Name),
                                NotificationType.Info,
                                () =>
                                {
                                    WindowOptions windowOptions = new WindowOptions
                                    {
                                        ShowMinimizeButton = false,
                                        ShowMaximizeButton = false,
                                        ShowCloseButton = true,
                                    };

                                    GameActivityBackup ViewExtension = new GameActivityBackup(backupData);
                                    Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(PlayniteApi, resources.GetString("LOCGaBackupDataInfo"), ViewExtension, windowOptions);
                                    windowExtension.ShowDialog();
                                }
                            ));
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }
        }

        // Add code to be executed when Playnite is shutting down.
        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            
        }
        #endregion

        // Add code to be executed when library is updated.
        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            
        }

        #region Settings
        public override ISettings GetSettings(bool firstRunSettings)
        {
            return PluginSettings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new GameActivitySettingsView();
        }
        #endregion

        #region LauhdutinImport
        public class LauhdutinGamesJson
        {
            public List<LauhdutinGame> games { get; set; }
        }

        public class LauhdutinGame
        {
            public string ti { get; set; } // Title
            public string tiOv { get; set; } // Title Override
            public int plID { get; set; } // PlatformID
            public string plOv { get; set; } // PlatformOverride
            public bool hi { get; set; } // Hidden
            public long laPl { get; set; } // LastPlayed
            public double hoPl { get; set; } // HoursPlayed
            public double hoLaPl { get; set; } // HoursLastPlay
            public string no { get; set; } // Notes
            public List<LauhdutinGamePrices> pri { get; set; } // Price
            public ulong tiPl { get; set; } // TimesPlayed
            public bool fa { get; set; } // Favorite
            public bool co { get; set; } // Complete
            public int? sc { get; set; } // Score
            public Dictionary<string, LauhdutinGameStats> st { get; set; } // Stats
        }

        public class LauhdutinGamePrices
        {
            public double pri { get; set; } // Price
            public string ca { get; set; } // Category
            public string ti { get; set; } // Title
            public string sT { get; set; } // Store
            public double da { get; set; } // Date
            public string bu { get; set; } // Bundle
            public string no { get; set; } // Notes
        }
        public class LauhdutinGameStats
        {
            public long daPl { get; set; } // DatePlayed
            public double hoPl { get; set; } // HoursPlayed
            public string no { get; set; } // Notes
        }

        public class LauhdutinToPlaynite
        {
            public string LauhdutinTitle { get; set; }
            public string PlayniteTitle { get; set; }
            public int PlatformId { get; set; } = 0;
            public int NewPlatformId { get; set; } = 0;
        }

        public int LauhdutinToPlaynitePlatformConversion(string gameTitle, int platformId)
        {
            List<LauhdutinToPlaynite> titleConverter = new List<LauhdutinToPlaynite>() {
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Destiny 2", PlayniteTitle = "Destiny 2", PlatformId = 5, NewPlatformId = 2},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "XIII - Classic", PlayniteTitle = "XIII - Classic", PlatformId = 1, NewPlatformId = 2},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Mortadelo y Filemon: El sulfato atomico", PlayniteTitle = "Mortadelo y Filemón: El sulfato atómico", PlatformId = 1, NewPlatformId = 2},
            };

            var newPlatform = titleConverter.FirstOrDefault(x => x.PlayniteTitle == gameTitle && x.PlatformId == platformId);
            if (newPlatform != null)
            {
                return newPlatform.NewPlatformId;
            }
            else
            {
                return platformId;
            }
        }

        public string LauhdutinToPlayniteTitleConversion(string gameTitle)
        {
            List<LauhdutinToPlaynite> titleConverter = new List<LauhdutinToPlaynite>() {
                new LauhdutinToPlaynite(){ LauhdutinTitle = "ABZU", PlayniteTitle = "ABZÛ"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Sherlock Holmes Consulting Detective: The Case of the Mummys Curse", PlayniteTitle = "Sherlock Holmes Consulting Detective: The Case of the Mummy’s Curse"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Mirrors Edge Catalyst", PlayniteTitle = "Mirror's Edge Catalyst"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "LEGO Indiana Jones: The Original Adventures", PlayniteTitle = "LEGOⓇ Indiana Jones: The Original Adventures"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Marc Eckos Getting Up: Contents Under Pressure", PlayniteTitle = "Marc Eckō's Getting Up: Contents Under Pressure"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Tormentor x Punisher", PlayniteTitle = "Tormentor❌Punisher"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Valiant Hearts: The Great War", PlayniteTitle = "Valiant Hearts: The Great War / Soldats Inconnus : Mémoires de la Grande Guerre"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Brutal Legend", PlayniteTitle = "Brütal Legend"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Mortadelo y Filemon: La banda de Corvino", PlayniteTitle = "Mortadelo y Filemón: La banda de Corvino"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Mortadelo y Filemon: Operacion Moscu", PlayniteTitle = "Mortadelo y Filemón: Operación Moscú"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Mortadelo y Filemon: Una aventura de cine - Edicion especial", PlayniteTitle = "Mortadelo y Filemón: Una aventura de cine - Edición especial"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Warcraft III: Reign of Chaos", PlayniteTitle = "Warcraft III: Reign of Chaos"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Warcraft III: The Frozen Throne", PlayniteTitle = "Warcraft III: The Frozen Throne"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Age of Empires: Definitive Edition", PlayniteTitle = "Age of Empires: Definitive Edition"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Kings Bounty: Legions", PlayniteTitle = "King’s Bounty: Legions"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Football Club Simulator - FCS NS19", PlayniteTitle = "Football Club Simulator - FCS #21"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Mortadelo y Filemon: La Maquina Meteoroloca", PlayniteTitle = "Mortadelo y Filemón: La Máquina Meteoroloca"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "XIII", PlayniteTitle = "XIII - Classic"},
                new LauhdutinToPlaynite(){ LauhdutinTitle = "Mortadelo y Filemon: El sulfato atomico", PlayniteTitle = "Mortadelo y Filemón: El sulfato atómico"},
            };

            var newTitle = titleConverter.FirstOrDefault(x => x.LauhdutinTitle == gameTitle);
            if (newTitle != null)
            {
                return newTitle.PlayniteTitle;
            }
            else
            {
                return gameTitle;
            }
        }

        // This method is copied from OnGameStarted and OnGameStopped adapted for the importing scenario
        public void CreateNewGameActivity(Game game, DateTime DateSession, ulong ElapsedSeconds, string platformName)
        {
            try
            {
                RunningActivity runningActivity = new RunningActivity();
                runningActivity.Id = game.Id;
                runningActivity.PlaytimeOnStarted = game.Playtime;

                runningActivities.Add(runningActivity);

                runningActivity.GameActivitiesLog = PluginDatabase.Get(game);
                runningActivity.GameActivitiesLog.Items.Add(new Activity
                {
                    IdConfiguration = PluginDatabase?.LocalSystem?.GetIdConfiguration() ?? -1,
                    GameActionName = platformName == "Riot Games" || platformName == "Windows shortcut" ? "Jugar" : resources.GetString("LOCGameActivityDefaultAction"),
                    DateSession = DateSession,
                    SourceID = game.SourceId == null ? default : game.SourceId,
                    PlatformIDs = game.PlatformIds ?? new List<Guid>(),
                    ElapsedSeconds = ElapsedSeconds
                });

                // Infos
                Common.LogDebug(true, Serialization.ToJson(runningActivity.GameActivitiesLog));
                PluginDatabase.Update(runningActivity.GameActivitiesLog);

                // Delete running data
                runningActivities.Remove(runningActivity);
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }
        }

        public void ImportLauhdutinGames(string type)
        {
            using (StreamReader r = new StreamReader("E:/games.json"))
            {
                string json = r.ReadToEnd();
                LauhdutinGamesJson items = JsonConvert.DeserializeObject<LauhdutinGamesJson>(json);
                if (type == "cleanPlayniteLibrary") // Set some properties to 0 / null
                {
                    logger.Info("Cleaning Playnite Library");
                    foreach (var playniteGame in PlayniteApi.Database.Games)
                        {
                            // Playtime
                            playniteGame.Playtime = 0;

                            // LastActivity
                            playniteGame.LastActivity = null;

                            // TimesPlayed
                            playniteGame.PlayCount = 0;

                            // Completed
                            playniteGame.CompletionStatusId = Guid.Parse("2609edc1-fb7e-4cf9-ad69-463cc79cb1ef"); // Sin jugar

                            // Favorite
                            playniteGame.Favorite = false;

                            // Score
                            playniteGame.UserScore = null;

                            // Notes
                            playniteGame.Notes = null;

                            PlayniteApi.Database.Games.Update(playniteGame); // Update the game after modificating it

                            logger.Info($"Cleaned game: {playniteGame.Name} {playniteGame.CompletionStatusId.ToString()}");
                        }
                    logger.Info("Cleaning Playnite Library finished");
                }
                else if (type == "makeTheImport") // Import the library from Lauhdutin to Playnite and shows what Lauhdutin games with timesPlayed or price are not present on Playnite Library.
                {
                    logger.Info("Importing Lauhdutin games to Playnite");
                    foreach (LauhdutinGame game in items.games)
                    {
                        // First check if exists TitleOverride. If so, compare this instead the title.
                        // Titles should be modified manually in Lauhdutin in order to be the same as on Playnite.
                        // These titles could also be manually added on the LauhdutinToPlayniteTitleConversion method, for bad characters like accents
                        string title = game.ti;
                        if (game.tiOv != null)
                        {
                            title = LauhdutinToPlayniteTitleConversion(game.tiOv);
                        }

                        // Also convert the platform for some games that were on another platform on Lauhdutin
                        game.plID = LauhdutinToPlaynitePlatformConversion(title, game.plID);

                        /* PlatformsIds / PlatformsSource -> Playnite: playniteGame.SourceId.ToString() / Lauhdutin: game.plID
                        Origin: Playnite "05bd84c8-d051-4226-ae76-5e21e8720c06" Lauhdutin 9
                        Steam: Playnite "21f997f9-9044-42e7-96b4-41fb85f407ec" Lauhdutin 2
                        Xbox: "7ed1c0fb-e14f-49bd-973d-887f99519d6f" Lauhdutin 14
                        Xbox Game Pass: Playnite "72d8c753-6237-4486-86fa-a412c0acd118" Lauhdutin ¿14?
                        Amazon: Playnite "80a1a4c6-d3ad-4c61-a598-bd152ac6e69f" Lauhdutin 3
                        Ubisoft Connect: Playnite "80c7e54d-b563-426a-b1ff-b0af75670e42" Lauhdutin 13 
                        Epic: Playnite "8adf1fb5-3565-4f36-b558-8f9aaf7318d2" Lauhdutin 7
                        Riot Games: Playnite "8c9e8671-0155-444a-9f08-62326110fd8f" Lauhdutin 11
                        Battle.net: Playnite "b555a171-d215-4df4-a238-ff9af575c722" Lauhdutin 5 
                        Humble: Playnite "b90e548e-347f-415c-a061-d7554e1ed9a4" Lauhdutin 8 
                        GOG: Playnite "cd6abd90-e4df-49d6-af7c-f4c07245197f" Lauhdutin 4

                        For Shortcuts, is 1 in Lauhdutin and IsCustomGame in Playnite
                        */

                        string platform;
                        string platformName;
                        switch (game.plID)
                        {
                            case 1: // Windows shortcut
                                platform = "";
                                platformName = "Windows shortcut";
                                break;
                            case 2: // Steam
                                platform = "21f997f9-9044-42e7-96b4-41fb85f407ec";
                                platformName = "Steam";
                                break;
                            case 3: // Amazon
                                platform = "80a1a4c6-d3ad-4c61-a598-bd152ac6e69f";
                                platformName = "Amazon";
                                break;
                            case 4: // GOG
                                platform = "cd6abd90-e4df-49d6-af7c-f4c07245197f";
                                platformName = "GOG";
                                break;
                            case 5: // Battle.net
                                platform = "b555a171-d215-4df4-a238-ff9af575c722";
                                platformName = "Battle.net";
                                break;
                            case 7: // Epic
                                platform = "8adf1fb5-3565-4f36-b558-8f9aaf7318d2";
                                platformName = "Epic";
                                break;
                            case 8: // Humble
                                platform = "b90e548e-347f-415c-a061-d7554e1ed9a4";
                                platformName = "Humble";
                                break;
                            case 9: // Origin
                                platform = "05bd84c8-d051-4226-ae76-5e21e8720c06";
                                platformName = "Origin";
                                break;
                            case 11: // Riot Games
                                platform = "8c9e8671-0155-444a-9f08-62326110fd8f";
                                platformName = "Riot Games";
                                break;
                            case 13: // Ubisoft Connect
                                platform = "80c7e54d-b563-426a-b1ff-b0af75670e42";
                                platformName = "Ubisoft Connect";
                                break;
                            case 14: // Xbox
                                platform = "7ed1c0fb-e14f-49bd-973d-887f99519d6f";
                                platformName = "Xbox";
                                break;
                            default:
                                platform = "";
                                platformName = "";
                                break;

                        }

                        bool isFound = false;

                        foreach (var playniteGame in PlayniteApi.Database.Games)
                        {
                            if (playniteGame.Name == title && (playniteGame.SourceId.ToString() == platform || game.plID == 1 && playniteGame.IsCustomGame))
                            {
                                // Hidden
                                if (game.hi)
                                {
                                    playniteGame.Hidden = game.hi;
                                }

                                // Favorite
                                if (game.fa)
                                {
                                    playniteGame.Favorite = game.fa;
                                }

                                // Notes
                                if (game.no != null)
                                {
                                    playniteGame.Notes = game.no;
                                }

                                // Score
                                if (game.sc != null)
                                {
                                    playniteGame.UserScore = game.sc * 10;
                                }

                                // Just add these stats if there is timesPlayed or hoursPlayed
                                if (game.tiPl > 0 || game.hoPl > 0)
                                {
                                    // Playtime
                                    // Convert double hoursPlayed to ulong secondsPlayed
                                    TimeSpan hoursPlayed = TimeSpan.FromHours(game.hoPl);
                                    ulong secondsPlayed = Convert.ToUInt64(hoursPlayed.TotalSeconds);
                                    playniteGame.Playtime = secondsPlayed;

                                    // LastActivity
                                    // Convert double datePlayed to DateTime datePlayed
                                    if (game.laPl != 0)
                                    {
                                        DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(game.laPl);
                                        DateTime lastActivity = dateTimeOffset.DateTime.ToLocalTime().ToUniversalTime();
                                        playniteGame.LastActivity = lastActivity;
                                    }
                                    else
                                    {
                                        playniteGame.LastActivity = null;
                                    }

                                    // TimesPlayed
                                    if (game.tiPl > 0)
                                    {
                                        playniteGame.PlayCount = game.tiPl;
                                    }

                                    // Completed
                                    if (game.co)
                                    {
                                        playniteGame.CompletionStatusId = Guid.Parse("f4000eca-c4aa-4677-b2b6-f891caa2c13f"); // Completado
                                    }
                                    else
                                    {
                                        playniteGame.CompletionStatusId = Guid.Parse("169e24a0-5b40-40cb-a46c-039d79d71cd4"); // Jugado
                                    }


                                    // Stats
                                    // Do the same conversions on stats
                                    if (game.st != null)
                                    {
                                        // Stats format: "i": { hoPl = hoursPlayed, daPl = datePlayed }
                                        foreach (KeyValuePair<string, LauhdutinGameStats> stat in game.st)
                                        {
                                            if (stat.Value.hoPl > 0)
                                            { 
                                                // Hours to seconds
                                                TimeSpan stHoursPlayed = TimeSpan.FromHours(stat.Value.hoPl);
                                                ulong stSecondsPlayed = Convert.ToUInt64(stHoursPlayed.TotalSeconds);

                                                // Epoch to DateTime
                                                DateTimeOffset stDateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(stat.Value.daPl);
                                                DateTime stLastActivity = stDateTimeOffset.DateTime.ToLocalTime().ToUniversalTime();

                                                // Create the activity based on the stat
                                                CreateNewGameActivity(playniteGame, stLastActivity, stSecondsPlayed, platformName);
                                            }
                                            else
                                            {
                                                logger.Debug($"Game {title} have a faulty stat: {stat.Key}");
                                            }
                                        }

                                    }
                                    else
                                    {
                                        logger.Debug($"{title} doesn't have stats");
                                    }
                                }
                                PlayniteApi.Database.Games.Update(playniteGame); // Update the game after modificating it
                                isFound = true;
                                break; // End with this game since is already merged on Playnite
                            }
                        }

                        if (!isFound && (game.tiPl > 0 || game.pri != null || game.hoPl > 0))
                        {
                            logger.Info($"Game {title} of platform {platformName} not found on Playnite database");
                        }
                    }
                }
                else if (type == "checkDifferences") // Check what Lauhdutin games are not detected on Playnite.
                {
                    logger.Debug("Checking if Lauhdutin games exists on Playnite Library");
                    foreach (LauhdutinGame game in items.games)
                    {
                        string title = game.ti;
                        if (game.tiOv != null)
                        {
                            title = LauhdutinToPlayniteTitleConversion(game.tiOv);
                        }

                        game.plID = LauhdutinToPlaynitePlatformConversion(title, game.plID);

                        string platform;
                        string platformName;
                        switch (game.plID)
                        {
                            case 1: // Windows shortcut
                                platform = "";
                                platformName = "Windows shortcut";
                                break;
                            case 2: // Steam
                                platform = "21f997f9-9044-42e7-96b4-41fb85f407ec";
                                platformName = "Steam";
                                break;
                            case 3: // Amazon
                                platform = "80a1a4c6-d3ad-4c61-a598-bd152ac6e69f";
                                platformName = "Amazon";
                                break;
                            case 4: // GOG
                                platform = "cd6abd90-e4df-49d6-af7c-f4c07245197f";
                                platformName = "GOG";
                                break;
                            case 5: // Battle.net
                                platform = "b555a171-d215-4df4-a238-ff9af575c722";
                                platformName = "Battle.net";
                                break;
                            case 7: // Epic
                                platform = "8adf1fb5-3565-4f36-b558-8f9aaf7318d2";
                                platformName = "Epic";
                                break;
                            case 8: // Humble
                                platform = "b90e548e-347f-415c-a061-d7554e1ed9a4";
                                platformName = "Humble";
                                break;
                            case 9: // Origin
                                platform = "05bd84c8-d051-4226-ae76-5e21e8720c06";
                                platformName = "Origin";
                                break;
                            case 11: // Riot Games
                                platform = "8c9e8671-0155-444a-9f08-62326110fd8f";
                                platformName = "Riot Games";
                                break;
                            case 13: // Ubisoft Connect
                                platform = "80c7e54d-b563-426a-b1ff-b0af75670e42";
                                platformName = "Ubisoft Connect";
                                break;
                            case 14: // Xbox
                                platform = "7ed1c0fb-e14f-49bd-973d-887f99519d6f";
                                platformName = "Xbox";
                                break;
                            default:
                                platform = "";
                                platformName = "";
                                break;
                        }

                        bool isFound = false;

                        foreach (var playniteGame in PlayniteApi.Database.Games)
                        {
                            if (playniteGame.Name == title && (playniteGame.SourceId.ToString() == platform || game.plID == 1 && playniteGame.IsCustomGame))
                            {
                                isFound = true;
                                break; // End with this game since is already found on Playnite
                            }
                        }

                        if (!isFound && (game.tiPl > 0 || game.pri != null || game.hoPl > 0))
                        {
                            logger.Info($"Game {title} of platform {platformName} not found on Playnite database");
                        }
                    }
                }
            }
        }
        #endregion
    }
}
