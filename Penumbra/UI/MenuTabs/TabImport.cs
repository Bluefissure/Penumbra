using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dalamud.Logging;
using ImGuiNET;
using Penumbra.Importer;
using Penumbra.Mods;
using Penumbra.UI.Custom;
using Penumbra.Util;

namespace Penumbra.UI
{
    public partial class SettingsInterface
    {
        private class TabImport
        {
            private const string LabelTab               = "导入模组";
            private const string LabelImportButton      = "导入 TexTools 模组包";
            private const string LabelFileDialog        = "选择一个或多个模组包.";
            private const string LabelFileImportRunning = "正在导入...";
            private const string FileTypeFilter         = "TexTools TTMP 模组包 (*.ttmp2)|*.ttmp*|All files (*.*)|*.*";
            private const string TooltipModpack1        = "在提取前将模组包写入磁盘...";

            private const uint ColorRed    = 0xFF0000C8;
            private const uint ColorYellow = 0xFF00C8C8;

            private static readonly Vector2 ImportBarSize = new( -1, 0 );

            private          bool              _isImportRunning;
            private          string            _errorMessage = string.Empty;
            private          TexToolsImport?   _texToolsImport;
            private readonly SettingsInterface _base;
            private readonly ModManager        _manager;

            public readonly HashSet< string > NewMods = new();

            public TabImport( SettingsInterface ui )
            {
                _base    = ui;
                _manager = Service< ModManager >.Get();
            }

            public bool IsImporting()
                => _isImportRunning;

            private void RunImportTask()
            {
                _isImportRunning = true;
                Task.Run( async () =>
                {
                    try
                    {
                        var picker = new OpenFileDialog
                        {
                            Multiselect     = true,
                            Filter          = FileTypeFilter,
                            CheckFileExists = true,
                            Title           = LabelFileDialog,
                        };

                        var result = await picker.ShowDialogAsync();

                        if( result == DialogResult.OK )
                        {
                            _errorMessage = string.Empty;

                            foreach( var fileName in picker.FileNames )
                            {
                                PluginLog.Information( $"-> {fileName} START" );

                                try
                                {
                                    _texToolsImport = new TexToolsImport( _manager.BasePath );
                                    var dir = _texToolsImport.ImportModPack( new FileInfo( fileName ) );
                                    if( dir.Name.Any() )
                                    {
                                        NewMods.Add( dir.Name );
                                    }

                                    PluginLog.Information( $"-> {fileName} OK!" );
                                }
                                catch( Exception ex )
                                {
                                    PluginLog.LogError( ex, "无法导入模组包文件 {0}", fileName );
                                    _errorMessage = ex.Message;
                                }
                            }

                            var directory = _texToolsImport?.ExtractedDirectory;
                            _texToolsImport = null;
                            _base.ReloadMods();
                            if( directory != null )
                            {
                                _base._menu.InstalledTab.Selector.SelectModOnUpdate( directory.Name );
                            }
                        }
                    }
                    catch( Exception e )
                    {
                        PluginLog.Error( $"无法打开文件选择对话框:\n{e}" );
                    }

                    _isImportRunning = false;
                } );
            }

            private void DrawImportButton()
            {
                if( !_manager.Valid )
                {
                    using var style = ImGuiRaii.PushStyle( ImGuiStyleVar.Alpha, 0.5f );
                    ImGui.Button( LabelImportButton );
                    style.Pop();

                    using var color = ImGuiRaii.PushColor( ImGuiCol.Text, ColorRed );
                    ImGui.Text( "无法进行导入 模组存放文件夹无效." );
                    ImGui.Dummy( Vector2.UnitY * ImGui.GetTextLineHeightWithSpacing() );
                    color.Pop();

                    ImGui.Text( "请在设置栏设定模组存放文件夹." );
                    ImGui.Text( "这个文件夹最好靠近你(最好是SSD)驱动器的根目录, 例如" );
                    color.Push( ImGuiCol.Text, ColorYellow );
                    ImGui.Text( "        D:\\ffxivmods" );
                    color.Pop();
                    ImGui.Text( "你如果已经做完了以上步骤请重新打开导入模组栏." );
                }
                else if( ImGui.Button( LabelImportButton ) )
                {
                    RunImportTask();
                }
            }

            private void DrawImportProgress()
            {
                ImGui.Button( LabelFileImportRunning );

                if( _texToolsImport == null )
                {
                    return;
                }

                switch( _texToolsImport.State )
                {
                    case ImporterState.None: break;
                    case ImporterState.WritingPackToDisk:
                        ImGui.Text( TooltipModpack1 );
                        break;
                    case ImporterState.ExtractingModFiles:
                    {
                        var str =
                            $"{_texToolsImport.CurrentModPack} - {_texToolsImport.CurrentProgress} of {_texToolsImport.TotalProgress} files";

                        ImGui.ProgressBar( _texToolsImport.Progress, ImportBarSize, str );
                        break;
                    }
                    case ImporterState.Done: break;
                    default:                 throw new ArgumentOutOfRangeException();
                }
            }

            private void DrawFailedImportMessage()
            {
                using var color = ImGuiRaii.PushColor( ImGuiCol.Text, ColorRed );
                ImGui.Text( $"一个或多个模组包导入失败:\n\t\t{_errorMessage}" );
            }

            public void Draw()
            {
                if( !ImGui.BeginTabItem( LabelTab ) )
                {
                    return;
                }

                using var raii = ImGuiRaii.DeferredEnd( ImGui.EndTabItem );

                if( !_isImportRunning )
                {
                    DrawImportButton();
                }
                else
                {
                    DrawImportProgress();
                }

                if( _errorMessage.Any() )
                {
                    DrawFailedImportMessage();
                }
            }
        }
    }
}