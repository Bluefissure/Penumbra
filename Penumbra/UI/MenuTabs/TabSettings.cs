using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Logging;
using ImGuiNET;
using Penumbra.Interop;
using Penumbra.UI.Custom;
using Penumbra.Util;

namespace Penumbra.UI;

public partial class SettingsInterface
{
    private class TabSettings
    {
        private readonly SettingsInterface _base;
        private readonly Configuration     _config;
        private          bool              _configChanged;
        private          string            _newModDirectory;
        private          string            _newTempDirectory;


        public TabSettings( SettingsInterface ui )
        {
            _base             = ui;
            _config           = Penumbra.Config;
            _configChanged    = false;
            _newModDirectory  = _config.ModDirectory;
            _newTempDirectory = _config.TempDirectory;
        }

        private static bool DrawPressEnterWarning( string old )
        {
            const uint red   = 0xFF202080;
            using var  color = ImGuiRaii.PushColor( ImGuiCol.Button, red );
            var        w     = Vector2.UnitX * ImGui.CalcItemWidth();
            return ImGui.Button( $"按下回车或者点这里保存 (当前目录: {old})", w );
        }

        private static void DrawOpenDirectoryButton( int id, DirectoryInfo directory, bool condition )
        {
            ImGui.PushID( id );
            var ret = ImGui.Button( "打开目录" );
            ImGuiCustom.HoverTooltip( "在你的默认资源管理器上显示此目录." );
            if( ret && condition && Directory.Exists( directory.FullName ) )
            {
                Process.Start( new ProcessStartInfo( directory.FullName )
                {
                    UseShellExecute = true,
                } );
            }

            ImGui.PopID();
        }

        private void DrawRootFolder()
        {
            ImGui.BeginGroup();
            ImGui.SetNextItemWidth( SettingsMenu.InputTextWidth );
            var save = ImGui.InputText( "根目录", ref _newModDirectory, 255, ImGuiInputTextFlags.EnterReturnsTrue );
            ImGui.SameLine();
            ImGuiComponents.HelpMarker( "此目录用于Penumbra存储提取的模组文件.\n"
              + "TTMP 并没有进行复制, 仅提取.\n"
              + "你需要有该目录的读写权限才能正常使用.\n"
              + "推荐将此文件夹存放至快速的磁盘, 比如说SSD.\n"
              + "也同样推荐将文件夹存放至最靠近磁盘根目录的地方 - 路径越短越好.\n"
              + "绝对不要放到Dalamud的根目录或其任何子目录." );
            ImGui.SameLine();
            DrawOpenDirectoryButton( 0, _base._modManager.BasePath, _base._modManager.Valid );
            ImGui.EndGroup();

            if( _config.ModDirectory == _newModDirectory || !_newModDirectory.Any() )
            {
                return;
            }

            if( save || DrawPressEnterWarning( _config.ModDirectory ) )
            {
                _base._menu.InstalledTab.Selector.ClearSelection();
                _base._modManager.DiscoverMods( _newModDirectory );
                _base._menu.InstalledTab.Selector.Cache.TriggerListReset();
                _newModDirectory = _config.ModDirectory;
            }
        }

        private void DrawTempFolder()
        {
            ImGui.BeginGroup();
            ImGui.SetNextItemWidth( SettingsMenu.InputTextWidth );
            var save = ImGui.InputText( "临时目录", ref _newTempDirectory, 255, ImGuiInputTextFlags.EnterReturnsTrue );
            ImGui.SameLine();
            ImGuiComponents.HelpMarker( "此目录用于Penumbra存储临时文件的地方.\n"
              + "如果你不知道是干啥的话留空就行.\n"
              + "目录 'penumbrametatmp' 将会在指定目录下生成.\n"
              + "如果没有指定目录 (例如留空) 该目录就会创建至设定的根目录下.\n" );
            ImGui.SameLine();
            DrawOpenDirectoryButton( 1, _base._modManager.TempPath, _base._modManager.TempWritable );
            ImGui.EndGroup();

            if( _newTempDirectory == _config.TempDirectory )
            {
                return;
            }

            if( save || DrawPressEnterWarning( _config.TempDirectory ) )
            {
                _base._modManager.SetTempDirectory( _newTempDirectory );
                _newTempDirectory = _config.TempDirectory;
            }
        }

        private void DrawRediscoverButton()
        {
            if( ImGui.Button( "刷新模组" ) )
            {
                _base._menu.InstalledTab.Selector.ClearSelection();
                _base._modManager.DiscoverMods();
                _base._menu.InstalledTab.Selector.Cache.TriggerListReset();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker( "强制Penumbra重新扫描根目录, 就像重新启动游戏一样." );
        }

        private void DrawEnabledBox()
        {
            var enabled = _config.IsEnabled;
            if( ImGui.Checkbox( "启用该模组", ref enabled ) )
            {
                _base._penumbra.SetEnabled( enabled );
            }
        }

        private void DrawShowAdvancedBox()
        {
            var showAdvanced = _config.ShowAdvanced;
            if( ImGui.Checkbox( "显示高级设置", ref showAdvanced ) )
            {
                _config.ShowAdvanced = showAdvanced;
                _configChanged       = true;
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker( "在这个窗口和模组选择器中启用一些高级选项.\n"
              + "这是必须的, 以启用手动编辑任何mod信息." );
        }

        private void DrawSortFoldersFirstBox()
        {
            var foldersFirst = _config.SortFoldersFirst;
            if( ImGui.Checkbox( "模组文件夹排序", ref foldersFirst ) )
            {
                _config.SortFoldersFirst = foldersFirst;
                _base._menu.InstalledTab.Selector.Cache.TriggerListReset();
                _configChanged = true;
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "在已安装模组选项卡的模组选择器中优先排序所有模组文件夹, 这样文件夹就会出现在单个模组之前, 而不是完全按照字母顺序排序" );
        }

        private void DrawScaleModSelectorBox()
        {
            var scaleModSelector = _config.ScaleModSelector;
            if( ImGui.Checkbox( "缩放模组选择器至窗口大小", ref scaleModSelector ) )
            {
                _config.ScaleModSelector = scaleModSelector;
                _configChanged           = true;
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "在已安装模组选项卡中, 模组选择器将保持固定的宽度, 这将让它随Penumbra窗口的总大小缩放." );
        }

        private void DrawDisableSoundStreamingBox()
        {
            var tmp = Penumbra.Config.DisableSoundStreaming;
            if( ImGui.Checkbox( "禁用音频流", ref tmp ) && tmp != Penumbra.Config.DisableSoundStreaming )
            {
                Penumbra.Config.DisableSoundStreaming = tmp;
                _configChanged                        = true;
                if( tmp )
                {
                    _base._penumbra.MusicManager.DisableStreaming();
                }
                else
                {
                    _base._penumbra.MusicManager.EnableStreaming();
                }

                _base.ReloadMods();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "禁用游戏音频引擎中的流媒体.\n"
              + "如果你不禁用流媒体, 你不能替换游戏中的声音文件 (*.scd 文件), 这些文件将会被Penumbra忽略.\n\n"
              + "只有在声音有问题时才碰这个.\n"
              + "如果你打开了此选项, 请确保当前或最近播放的声音文件中没有修改或要修改的声音文件, 否则游戏可能会崩溃." );
        }

        private void DrawLogLoadedFilesBox()
        {
            ImGui.Checkbox( "记录已加载的文件", ref _base._penumbra.ResourceLoader.LogAllFiles );
            ImGui.SameLine();
            var regex = _base._penumbra.ResourceLoader.LogFileFilter?.ToString() ?? string.Empty;
            var tmp   = regex;
            ImGui.SetNextItemWidth( SettingsMenu.InputTextWidth );
            if( ImGui.InputTextWithHint( "##LogFilter", "符合此正则表达式...", ref tmp, 64 ) && tmp != regex )
            {
                try
                {
                    var newRegex = tmp.Length > 0 ? new Regex( tmp, RegexOptions.Compiled ) : null;
                    _base._penumbra.ResourceLoader.LogFileFilter = newRegex;
                }
                catch( Exception e )
                {
                    PluginLog.Debug( "无法创建此正则表达式:\n{Exception}", e );
                }
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker( "将所有与正则表达式匹配的加载文件记录到PluginLog中." );
        }

        private void DrawDisableNotificationsBox()
        {
            var fsWatch = _config.DisableFileSystemNotifications;
            if( ImGui.Checkbox( "禁用文件系统更改通知", ref fsWatch ) )
            {
                _config.DisableFileSystemNotifications = fsWatch;
                _configChanged                         = true;
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker( "目前没啥用." );
        }

        private void DrawEnableHttpApiBox()
        {
            var http = _config.EnableHttpApi;
            if( ImGui.Checkbox( "启用 HTTP API", ref http ) )
            {
                if( http )
                {
                    _base._penumbra.CreateWebServer();
                }
                else
                {
                    _base._penumbra.ShutdownWebServer();
                }

                _config.EnableHttpApi = http;
                _configChanged        = true;
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker( "目前没啥用." );
        }

        private void DrawEnabledPlayerWatcher()
        {
            var enabled = _config.EnablePlayerWatch;
            if( ImGui.Checkbox( "启用自动角色重绘", ref enabled ) )
            {
                _config.EnablePlayerWatch = enabled;
                _configChanged            = true;
                Penumbra.PlayerWatcher.SetStatus( enabled );
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "If this setting is enabled, Penumbra will keep tabs on characters that have a corresponding character collection setup in the Collections tab.\n"
              + "Penumbra will try to automatically redraw those characters using their collection when they first appear in an instance, or when they change their current equip.\n" );

            if( !_config.EnablePlayerWatch || !_config.ShowAdvanced )
            {
                return;
            }

            var waitFrames = _config.WaitFrames;
            ImGui.SameLine();
            ImGui.SetNextItemWidth( 50 * ImGuiHelpers.GlobalScale );
            if( ImGui.InputInt( "Wait Frames", ref waitFrames, 0, 0 )
            && waitFrames != _config.WaitFrames
            && waitFrames is > 0 and < 3000 )
            {
                _base._penumbra.ObjectReloader.DefaultWaitFrames = waitFrames;
                _config.WaitFrames                               = waitFrames;
                _configChanged                                   = true;
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "The number of frames penumbra waits after some events (like zone changes) until it starts trying to redraw actors again, in a range of [1, 3001].\n"
              + "Keep this as low as possible while producing stable results." );
        }

        private static void DrawReloadResourceButton()
        {
            if( ImGui.Button( "重载常驻文件" ) )
            {
                Service< ResidentResources >.Get().ReloadResidentResources();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker( "重新加载游戏始终保存在内存中的某些特定文件.\n"
              + "通常情况下不需要操作." );
        }

        private void DrawAdvancedSettings()
        {
            DrawTempFolder();
            DrawDisableSoundStreamingBox();
            DrawLogLoadedFilesBox();
            DrawDisableNotificationsBox();
            DrawEnableHttpApiBox();
            DrawReloadResourceButton();
        }

        public void Draw()
        {
            if( !ImGui.BeginTabItem( "设置" ) )
            {
                return;
            }

            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndTabItem );

            DrawRootFolder();

            DrawRediscoverButton();

            ImGuiCustom.VerticalDistance( DefaultVerticalSpace );
            DrawEnabledBox();
            DrawEnabledPlayerWatcher();

            ImGuiCustom.VerticalDistance( DefaultVerticalSpace );
            DrawScaleModSelectorBox();
            DrawSortFoldersFirstBox();
            DrawShowAdvancedBox();

            if( _config.ShowAdvanced )
            {
                DrawAdvancedSettings();
            }

            if( _configChanged )
            {
                _config.Save();
                _configChanged = false;
            }
        }
    }
}