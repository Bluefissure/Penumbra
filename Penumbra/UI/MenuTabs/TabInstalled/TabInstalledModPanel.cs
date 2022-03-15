using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Logging;
using ImGuiNET;
using Penumbra.Mod;
using Penumbra.Mods;
using Penumbra.UI.Custom;
using Penumbra.Util;

namespace Penumbra.UI;

public partial class SettingsInterface
{
    private class ModPanel
    {
        private const string LabelModPanel          = "selectedModInfo";
        private const string LabelEditName          = "##editName";
        private const string LabelEditVersion       = "##editVersion";
        private const string LabelEditAuthor        = "##editAuthor";
        private const string LabelEditWebsite       = "##editWebsite";
        private const string LabelModEnabled        = "已启用";
        private const string LabelEditingEnabled    = "启用编辑";
        private const string LabelOverWriteDir      = "OverwriteDir";
        private const string ButtonOpenWebsite      = "打开网页";
        private const string ButtonOpenModFolder    = "打开模组文件夹";
        private const string ButtonRenameModFolder  = "重命名模组文件夹";
        private const string ButtonEditJson         = "编辑 JSON";
        private const string ButtonReloadJson       = "重载 JSON";
        private const string ButtonDeduplicate      = "除重";
        private const string ButtonNormalize        = "Normalize";
        private const string TooltipOpenModFolder   = "在默认的文件管理器中打开包含此模组的目录.";
        private const string TooltipRenameModFolder = "在不打开另一个应用程序的情况下重命名包含此模组的目录.";
        private const string TooltipEditJson        = "在默认的应用程序中打开JSON配置文件.";
        private const string TooltipReloadJson      = "重新加载所有模组的配置.";
        private const string PopupRenameFolder      = "重命名文件夹";

        private const string TooltipDeduplicate =
            "尝试找到相同的文件, 并删除重复的文件, 以减少模组占用大小.\n"
          + "引入不可见的单选项组 \"重复文件\".\n实验性功能 - 后果自负!";

        private const string TooltipNormalize =
            "尽量减少不必要的选项或子目录为默认选项.\n实验性功能 - 后果自负!";

        private const           float   HeaderLineDistance = 10f;
        private static readonly Vector4 GreyColor          = new(1f, 1f, 1f, 0.66f);

        private readonly SettingsInterface _base;
        private readonly Selector          _selector;
        private readonly ModManager        _modManager;
        private readonly HashSet< string > _newMods;
        public readonly  PluginDetails     Details;

        private bool   _editMode;
        private string _currentWebsite;
        private bool   _validWebsite;

        private string _fromMaterial = string.Empty;
        private string _toMaterial   = string.Empty;

        public ModPanel( SettingsInterface ui, Selector s, HashSet< string > newMods )
        {
            _base           = ui;
            _selector       = s;
            _newMods        = newMods;
            Details         = new PluginDetails( _base, _selector );
            _currentWebsite = Meta?.Website ?? "";
            _modManager     = Service< ModManager >.Get();
        }

        private Mod.Mod? Mod
            => _selector.Mod;

        private ModMeta? Meta
            => Mod?.Data.Meta;

        private void DrawName()
        {
            var name = Meta!.Name;
            if( ImGuiCustom.InputOrText( _editMode, LabelEditName, ref name, 64 ) && _modManager.RenameMod( name, Mod!.Data ) )
            {
                _selector.SelectModOnUpdate( Mod.Data.BasePath.Name );
                if( !_modManager.Config.ModSortOrder.ContainsKey( Mod!.Data.BasePath.Name ) )
                {
                    Mod.Data.Rename( name );
                }
            }
        }

        private void DrawVersion()
        {
            if( _editMode )
            {
                ImGui.BeginGroup();
                using var raii = ImGuiRaii.DeferredEnd( ImGui.EndGroup );
                ImGui.Text( "(版本 " );

                using var style = ImGuiRaii.PushStyle( ImGuiStyleVar.ItemSpacing, ZeroVector );
                ImGui.SameLine();
                var version = Meta!.Version;
                if( ImGuiCustom.ResizingTextInput( LabelEditVersion, ref version, 16 )
                && version != Meta.Version )
                {
                    Meta.Version = version;
                    _selector.SaveCurrentMod();
                }

                ImGui.SameLine();
                ImGui.Text( ")" );
            }
            else if( Meta!.Version.Length > 0 )
            {
                ImGui.Text( $"(版本 {Meta.Version})" );
            }
        }

        private void DrawAuthor()
        {
            ImGui.BeginGroup();
            ImGui.TextColored( GreyColor, "by" );

            ImGui.SameLine();
            var author = Meta!.Author;
            if( ImGuiCustom.InputOrText( _editMode, LabelEditAuthor, ref author, 64 )
            && author != Meta.Author )
            {
                Meta.Author = author;
                _selector.SaveCurrentMod();
                _selector.Cache.TriggerFilterReset();
            }

            ImGui.EndGroup();
        }

        private void DrawWebsite()
        {
            ImGui.BeginGroup();
            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndGroup );
            if( _editMode )
            {
                ImGui.TextColored( GreyColor, "from" );
                ImGui.SameLine();
                var website = Meta!.Website;
                if( ImGuiCustom.ResizingTextInput( LabelEditWebsite, ref website, 512 )
                && website != Meta.Website )
                {
                    Meta.Website = website;
                    _selector.SaveCurrentMod();
                }
            }
            else if( Meta!.Website.Length > 0 )
            {
                if( _currentWebsite != Meta.Website )
                {
                    _currentWebsite = Meta.Website;
                    _validWebsite = Uri.TryCreate( Meta.Website, UriKind.Absolute, out var uriResult )
                     && ( uriResult.Scheme == Uri.UriSchemeHttps || uriResult.Scheme == Uri.UriSchemeHttp );
                }

                if( _validWebsite )
                {
                    if( ImGui.SmallButton( ButtonOpenWebsite ) )
                    {
                        try
                        {
                            var process = new ProcessStartInfo( Meta.Website )
                            {
                                UseShellExecute = true,
                            };
                            Process.Start( process );
                        }
                        catch( System.ComponentModel.Win32Exception )
                        {
                            // Do nothing.
                        }
                    }

                    ImGuiCustom.HoverTooltip( Meta.Website );
                }
                else
                {
                    ImGui.TextColored( GreyColor, "from" );
                    ImGui.SameLine();
                    ImGui.Text( Meta.Website );
                }
            }
        }

        private void DrawHeaderLine()
        {
            DrawName();
            ImGui.SameLine();
            DrawVersion();
            ImGui.SameLine();
            DrawAuthor();
            ImGui.SameLine();
            DrawWebsite();
        }

        private void DrawPriority()
        {
            var priority = Mod!.Settings.Priority;
            ImGui.SetNextItemWidth( 50 * ImGuiHelpers.GlobalScale );
            if( ImGui.InputInt( "加载优先级", ref priority, 0 ) && priority != Mod!.Settings.Priority )
            {
                Mod.Settings.Priority = priority;
                _base.SaveCurrentCollection( Mod.Data.Resources.MetaManipulations.Count > 0 );
                _selector.Cache.TriggerFilterReset();
            }

            ImGuiCustom.HoverTooltip(
                "在文件冲突的情况下, 优先级更高的模组会优先于其他模组的加载.\n"
              + "在相同优先级情况下会根据字母决定加载顺序." );
        }

        private void DrawEnabledMark()
        {
            var enabled = Mod!.Settings.Enabled;
            if( ImGui.Checkbox( LabelModEnabled, ref enabled ) )
            {
                Mod.Settings.Enabled = enabled;
                if( enabled )
                {
                    _newMods.Remove( Mod.Data.BasePath.Name );
                }
                else
                {
                    Mod.Cache.ClearConflicts();
                }

                _base.SaveCurrentCollection( Mod.Data.Resources.MetaManipulations.Count > 0 );
                _selector.Cache.TriggerFilterReset();
            }
        }

        public static bool DrawSortOrder( ModData mod, ModManager manager, Selector selector )
        {
            var currentSortOrder = mod.SortOrder.FullPath;
            ImGui.SetNextItemWidth( 300 * ImGuiHelpers.GlobalScale );
            if( ImGui.InputText( "排序顺序", ref currentSortOrder, 256, ImGuiInputTextFlags.EnterReturnsTrue ) )
            {
                manager.ChangeSortOrder( mod, currentSortOrder );
                selector.SelectModOnUpdate( mod.BasePath.Name );
                return true;
            }

            return false;
        }

        private void DrawEditableMark()
        {
            ImGui.Checkbox( LabelEditingEnabled, ref _editMode );
        }

        private void DrawOpenModFolderButton()
        {
            Mod!.Data.BasePath.Refresh();
            if( ImGui.Button( ButtonOpenModFolder ) && Mod.Data.BasePath.Exists )
            {
                Process.Start( new ProcessStartInfo( Mod!.Data.BasePath.FullName ) { UseShellExecute = true } );
            }

            ImGuiCustom.HoverTooltip( TooltipOpenModFolder );
        }

        private string _newName       = "";
        private bool   _keyboardFocus = true;

        private void RenameModFolder( string newName )
        {
            _newName = newName.ReplaceBadXivSymbols();
            if( _newName.Length == 0 )
            {
                PluginLog.Debug( "New Directory name {NewName} was empty after removing invalid symbols.", newName );
                ImGui.CloseCurrentPopup();
            }
            else if( !string.Equals( _newName, Mod!.Data.BasePath.Name, StringComparison.InvariantCultureIgnoreCase ) )
            {
                var           dir    = Mod!.Data.BasePath;
                DirectoryInfo newDir = new(Path.Combine( dir.Parent!.FullName, _newName ));

                if( newDir.Exists )
                {
                    ImGui.OpenPopup( LabelOverWriteDir );
                }
                else if( _modManager.RenameModFolder( Mod.Data, newDir ) )
                {
                    _selector.ReloadCurrentMod();
                    ImGui.CloseCurrentPopup();
                }
            }
            else if( !string.Equals( _newName, Mod!.Data.BasePath.Name, StringComparison.InvariantCulture ) )
            {
                var           dir       = Mod!.Data.BasePath;
                DirectoryInfo newDir    = new(Path.Combine( dir.Parent!.FullName, _newName ));
                var           sourceUri = new Uri( dir.FullName );
                var           targetUri = new Uri( newDir.FullName );
                if( sourceUri.Equals( targetUri ) )
                {
                    var tmpFolder = new DirectoryInfo( TempFile.TempFileName( dir.Parent! ).FullName );
                    if( _modManager.RenameModFolder( Mod.Data, tmpFolder ) )
                    {
                        if( !_modManager.RenameModFolder( Mod.Data, newDir ) )
                        {
                            PluginLog.Error( "重命名后无法重新调整文件夹, 撤销重命名." );
                            _modManager.RenameModFolder( Mod.Data, dir );
                        }

                        _selector.ReloadCurrentMod();
                    }

                    ImGui.CloseCurrentPopup();
                }
                else
                {
                    ImGui.OpenPopup( LabelOverWriteDir );
                }
            }
        }

        private static bool MergeFolderInto( DirectoryInfo source, DirectoryInfo target )
        {
            try
            {
                foreach( var file in source.EnumerateFiles( "*", SearchOption.AllDirectories ) )
                {
                    var targetFile = new FileInfo( Path.Combine( target.FullName, file.FullName.Substring( source.FullName.Length + 1 ) ) );
                    if( targetFile.Exists )
                    {
                        targetFile.Delete();
                    }

                    targetFile.Directory?.Create();
                    file.MoveTo( targetFile.FullName );
                }

                source.Delete( true );
                return true;
            }
            catch( Exception e )
            {
                PluginLog.Error( $"无法合并文件夹 {source.FullName} 至 {target.FullName}:\n{e}" );
            }

            return false;
        }

        private bool OverwriteDirPopup()
        {
            var closeParent = false;
            var _           = true;
            if( !ImGui.BeginPopupModal( LabelOverWriteDir, ref _, ImGuiWindowFlags.AlwaysAutoResize ) )
            {
                return closeParent;
            }

            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndPopup );

            var           dir    = Mod!.Data.BasePath;
            DirectoryInfo newDir = new(Path.Combine( dir.Parent!.FullName, _newName ));
            ImGui.Text(
                $"模组文件夹 {newDir} 已存在.\n你确定要合并/覆写两个模组?\n这可能会以不可挽回的方式破坏模组." );
            var buttonSize = ImGuiHelpers.ScaledVector2( 120, 0 );
            if( ImGui.Button( "Yes", buttonSize ) )
            {
                if( MergeFolderInto( dir, newDir ) )
                {
                    Service< ModManager >.Get()!.RenameModFolder( Mod.Data, newDir, false );

                    _selector.SelectModOnUpdate( _newName );

                    closeParent = true;
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.SameLine();

            if( ImGui.Button( "取消", buttonSize ) )
            {
                _keyboardFocus = true;
                ImGui.CloseCurrentPopup();
            }

            return closeParent;
        }

        private void DrawRenameModFolderPopup()
        {
            var _ = true;
            _keyboardFocus |= !ImGui.IsPopupOpen( PopupRenameFolder );

            ImGui.SetNextWindowPos( ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2( 0.5f, 1f ) );
            if( !ImGui.BeginPopupModal( PopupRenameFolder, ref _, ImGuiWindowFlags.AlwaysAutoResize ) )
            {
                return;
            }

            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndPopup );

            if( ImGui.IsKeyPressed( ImGui.GetKeyIndex( ImGuiKey.Escape ) ) )
            {
                ImGui.CloseCurrentPopup();
            }

            var newName = Mod!.Data.BasePath.Name;

            if( _keyboardFocus )
            {
                ImGui.SetKeyboardFocusHere();
                _keyboardFocus = false;
            }

            if( ImGui.InputText( "New Folder Name##RenameFolderInput", ref newName, 64, ImGuiInputTextFlags.EnterReturnsTrue ) )
            {
                RenameModFolder( newName );
            }

            ImGui.TextColored( GreyColor,
                "请限制自己使用在Windows路径中有效的ascii符号,\n其他符号会被下划线代替." );

            ImGui.SetNextWindowPos( ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, Vector2.One / 2 );


            if( OverwriteDirPopup() )
            {
                ImGui.CloseCurrentPopup();
            }
        }

        private void DrawRenameModFolderButton()
        {
            DrawRenameModFolderPopup();
            if( ImGui.Button( ButtonRenameModFolder ) )
            {
                ImGui.OpenPopup( PopupRenameFolder );
            }

            ImGuiCustom.HoverTooltip( TooltipRenameModFolder );
        }

        private void DrawEditJsonButton()
        {
            if( ImGui.Button( ButtonEditJson ) )
            {
                _selector.SaveCurrentMod();
                Process.Start( new ProcessStartInfo( Mod!.Data.MetaFile.FullName ) { UseShellExecute = true } );
            }

            ImGuiCustom.HoverTooltip( TooltipEditJson );
        }

        private void DrawReloadJsonButton()
        {
            if( ImGui.Button( ButtonReloadJson ) )
            {
                _selector.ReloadCurrentMod( true, false );
            }

            ImGuiCustom.HoverTooltip( TooltipReloadJson );
        }

        private void DrawResetMetaButton()
        {
            if( ImGui.Button( "Recompute Metadata" ) )
            {
                _selector.ReloadCurrentMod( true, true, true );
            }

            ImGuiCustom.HoverTooltip(
                "Force a recomputation of the metadata_manipulations.json file from all .meta files in the folder.\n"
              + "Also reloads the mod.\n"
              + "Be aware that this removes all manually added metadata changes." );
        }

        private void DrawDeduplicateButton()
        {
            if( ImGui.Button( ButtonDeduplicate ) )
            {
                ModCleanup.Deduplicate( Mod!.Data.BasePath, Meta! );
                _selector.SaveCurrentMod();
                _selector.ReloadCurrentMod( true, true, true );
            }

            ImGuiCustom.HoverTooltip( TooltipDeduplicate );
        }

        private void DrawNormalizeButton()
        {
            if( ImGui.Button( ButtonNormalize ) )
            {
                ModCleanup.Normalize( Mod!.Data.BasePath, Meta! );
                _selector.SaveCurrentMod();
                _selector.ReloadCurrentMod( true, true, true );
            }

            ImGuiCustom.HoverTooltip( TooltipNormalize );
        }

        private void DrawAutoGenerateGroupsButton()
        {
            if( ImGui.Button( "Auto-Generate Groups" ) )
            {
                ModCleanup.AutoGenerateGroups( Mod!.Data.BasePath, Meta! );
                _selector.SaveCurrentMod();
                _selector.ReloadCurrentMod( true, true );
            }

            ImGuiCustom.HoverTooltip( "Automatically generate single-select groups from all folders (clears existing groups):\n"
              + "First subdirectory: Option Group\n"
              + "Second subdirectory: Option Name\n"
              + "Afterwards: Relative file paths.\n"
              + "Experimental - Use at own risk!" );
        }

        private void DrawSplitButton()
        {
            if( ImGui.Button( "Split Mod" ) )
            {
                ModCleanup.SplitMod( Mod!.Data );
            }

            ImGuiCustom.HoverTooltip(
                "Split off all options of a mod into single mods that are placed in a collective folder.\n"
              + "Does not remove or change the mod itself, just create (potentially inefficient) copies.\n"
              + "Experimental - Use at own risk!" );
        }

        private void DrawMaterialChangeRow()
        {
            ImGui.SetNextItemWidth( 150 * ImGuiHelpers.GlobalScale );
            ImGui.InputTextWithHint( "##fromMaterial", "From Material Suffix...", ref _fromMaterial, 16 );
            ImGui.SameLine();
            using var font = ImGuiRaii.PushFont( UiBuilder.IconFont );
            ImGui.Text( FontAwesomeIcon.LongArrowAltRight.ToIconString() );
            font.Pop();
            ImGui.SameLine();
            ImGui.SetNextItemWidth( 150 * ImGuiHelpers.GlobalScale );
            ImGui.InputTextWithHint( "##toMaterial", "To Material Suffix...", ref _toMaterial, 16 );
            ImGui.SameLine();
            var       validStrings = ModelChanger.ValidStrings( _fromMaterial, _toMaterial );
            using var alpha        = ImGuiRaii.PushStyle( ImGuiStyleVar.Alpha, 0.5f, !validStrings );
            if( ImGui.Button( "Convert" ) && validStrings )
            {
                ModelChanger.ChangeModMaterials( Mod!.Data, _fromMaterial, _toMaterial );
            }

            alpha.Pop();

            ImGuiCustom.HoverTooltip(
                "Change the skin material of all models in this mod reference "
              + "from the suffix given in the first text input to "
              + "the suffix given in the second input.\n"
              + "Enter only the suffix, e.g. 'd' or 'a' or 'bibo', not the whole path.\n"
              + "This overwrites .mdl files, use at your own risk!" );
        }

        private void DrawEditLine()
        {
            DrawOpenModFolderButton();
            ImGui.SameLine();
            DrawRenameModFolderButton();
            ImGui.SameLine();
            DrawEditJsonButton();
            ImGui.SameLine();
            DrawReloadJsonButton();

            DrawResetMetaButton();
            ImGui.SameLine();
            DrawDeduplicateButton();
            ImGui.SameLine();
            DrawNormalizeButton();
            ImGui.SameLine();
            DrawAutoGenerateGroupsButton();
            ImGui.SameLine();
            DrawSplitButton();

            DrawMaterialChangeRow();

            DrawSortOrder( Mod!.Data, _modManager, _selector );
        }

        public void Draw()
        {
            try
            {
                using var raii = ImGuiRaii.DeferredEnd( ImGui.EndChild );
                var       ret  = ImGui.BeginChild( LabelModPanel, AutoFillSize, true );

                if( !ret || Mod == null )
                {
                    return;
                }

                DrawHeaderLine();

                // Next line with fixed distance.
                ImGuiCustom.VerticalDistance( HeaderLineDistance );

                DrawEnabledMark();
                ImGui.SameLine();
                DrawPriority();
                if( Penumbra.Config.ShowAdvanced )
                {
                    ImGui.SameLine();
                    DrawEditableMark();
                }

                // Next line, if editable.
                if( _editMode )
                {
                    DrawEditLine();
                }

                Details.Draw( _editMode );
            }
            catch( Exception ex )
            {
                PluginLog.LogError( ex, "Oh no" );
            }
        }
    }
}