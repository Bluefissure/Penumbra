using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms.VisualStyles;
using Dalamud.Interface;
using Dalamud.Logging;
using ImGuiNET;
using Penumbra.Importer;
using Penumbra.Mod;
using Penumbra.Mods;
using Penumbra.UI.Custom;
using Penumbra.Util;

namespace Penumbra.UI;

public partial class SettingsInterface
{
    // Constants
    private partial class Selector
    {
        private const string LabelSelectorList = "##availableModList";
        private const string LabelModFilter    = "##ModFilter";
        private const string LabelAddModPopup  = "AddModPopup";
        private const string LabelModHelpPopup = "Help##Selector";

        private const string TooltipModFilter =
            "过滤包含给定子字符串的模组.\n输入 c:[字符串] 来搜寻替换特定物品的模组.\n输入 a:[字符串] 来搜寻特定作者的模组.";

        private const string TooltipDelete   = "删除选中的模组";
        private const string TooltipAdd      = "添加空白的模组";
        private const string DialogDeleteMod = "PenumbraDeleteMod";
        private const string ButtonYesDelete = "确定删除";
        private const string ButtonNoDelete  = "算了";

        private const float SelectorPanelWidth = 240f;

        private static readonly Vector2 SelectorButtonSizes = new(100, 0);
        private static readonly Vector2 HelpButtonSizes     = new(40, 0);

        private static readonly Vector4 DeleteModNameColor = new(0.7f, 0.1f, 0.1f, 1);
    }

    // Buttons
    private partial class Selector
    {
        // === Delete ===
        private int? _deleteIndex;

        private void DrawModTrashButton()
        {
            using var raii = ImGuiRaii.PushFont( UiBuilder.IconFont );

            if( ImGui.Button( FontAwesomeIcon.Trash.ToIconString(), SelectorButtonSizes * _selectorScalingFactor ) && _index >= 0 )
            {
                _deleteIndex = _index;
            }

            raii.Pop();

            ImGuiCustom.HoverTooltip( TooltipDelete );
        }

        private void DrawDeleteModal()
        {
            if( _deleteIndex == null )
            {
                return;
            }

            ImGui.OpenPopup( DialogDeleteMod );

            var _ = true;
            ImGui.SetNextWindowPos( ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, Vector2.One / 2 );
            var ret = ImGui.BeginPopupModal( DialogDeleteMod, ref _, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDecoration );
            if( !ret )
            {
                return;
            }

            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndPopup );

            if( Mod == null )
            {
                _deleteIndex = null;
                ImGui.CloseCurrentPopup();
                return;
            }

            ImGui.Text( "你确定要删除以下模组吗:" );
            var halfLine = new Vector2( ImGui.GetTextLineHeight() / 2 );
            ImGui.Dummy( halfLine );
            ImGui.TextColored( DeleteModNameColor, Mod.Data.Meta.Name );
            ImGui.Dummy( halfLine );

            var buttonSize = ImGuiHelpers.ScaledVector2( 120, 0 );
            if( ImGui.Button( ButtonYesDelete, buttonSize ) )
            {
                ImGui.CloseCurrentPopup();
                var mod = Mod;
                Cache.RemoveMod( mod );
                _modManager.DeleteMod( mod.Data.BasePath );
                ModFileSystem.InvokeChange();
                ClearSelection();
            }

            ImGui.SameLine();

            if( ImGui.Button( ButtonNoDelete, buttonSize ) )
            {
                ImGui.CloseCurrentPopup();
                _deleteIndex = null;
            }
        }

        // === Add ===
        private bool _modAddKeyboardFocus = true;

        private void DrawModAddButton()
        {
            using var raii = ImGuiRaii.PushFont( UiBuilder.IconFont );

            if( ImGui.Button( FontAwesomeIcon.Plus.ToIconString(), SelectorButtonSizes * _selectorScalingFactor ) )
            {
                _modAddKeyboardFocus = true;
                ImGui.OpenPopup( LabelAddModPopup );
            }

            raii.Pop();

            ImGuiCustom.HoverTooltip( TooltipAdd );

            DrawModAddPopup();
        }

        private void DrawModAddPopup()
        {
            if( !ImGui.BeginPopup( LabelAddModPopup ) )
            {
                return;
            }

            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndPopup );

            if( _modAddKeyboardFocus )
            {
                ImGui.SetKeyboardFocusHere();
                _modAddKeyboardFocus = false;
            }

            var newName = "";
            if( ImGui.InputTextWithHint( "##AddMod", "新模组名称...", ref newName, 64, ImGuiInputTextFlags.EnterReturnsTrue ) )
            {
                try
                {
                    var newDir = TexToolsImport.CreateModFolder( new DirectoryInfo( Penumbra.Config!.ModDirectory ),
                        newName );
                    var modMeta = new ModMeta
                    {
                        Author      = "未知",
                        Name        = newName.Replace( '/', '\\' ),
                        Description = string.Empty,
                    };

                    var metaFile = new FileInfo( Path.Combine( newDir.FullName, "meta.json" ) );
                    modMeta.SaveToFile( metaFile );
                    _modManager.AddMod( newDir );
                    ModFileSystem.InvokeChange();
                    SelectModOnUpdate( newDir.Name );
                }
                catch( Exception e )
                {
                    PluginLog.Error( $"无法为新模组新建文件夹 {newName}:\n{e}" );
                }

                ImGui.CloseCurrentPopup();
            }

            if( ImGui.IsKeyPressed( ImGui.GetKeyIndex( ImGuiKey.Escape ) ) )
            {
                ImGui.CloseCurrentPopup();
            }
        }

        // === Help ===
        private void DrawModHelpButton()
        {
            using var raii = ImGuiRaii.PushFont( UiBuilder.IconFont );
            if( ImGui.Button( FontAwesomeIcon.QuestionCircle.ToIconString(), HelpButtonSizes * _selectorScalingFactor ) )
            {
                ImGui.OpenPopup( LabelModHelpPopup );
            }
        }

        private static void DrawModHelpPopup()
        {
            ImGui.SetNextWindowPos( ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, Vector2.One / 2 );
            ImGui.SetNextWindowSize( new Vector2( 5 * SelectorPanelWidth, 34 * ImGui.GetTextLineHeightWithSpacing() ),
                ImGuiCond.Appearing );
            var _ = true;
            if( !ImGui.BeginPopupModal( LabelModHelpPopup, ref _, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove ) )
            {
                return;
            }

            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndPopup );

            ImGui.Dummy( Vector2.UnitY * ImGui.GetTextLineHeight() );
            ImGui.Text( "模组选择" );
            ImGui.BulletText( "选择一个模组来获取更多信息." );
            ImGui.BulletText( "模组名称根据它们在合集中的当前状态着色:" );
            ImGui.Indent();
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.Text( "已在当前合集中启用." );
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextColored( ImGui.ColorConvertU32ToFloat4( ModListCache.DisabledModColor ), "已在当前合集中禁用." );
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextColored( ImGui.ColorConvertU32ToFloat4( ModListCache.NewModColor ),
                "刚刚导入的模组. 在第一次启用模组时或Penumbra重新加载时就会消失." );
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextColored( ImGui.ColorConvertU32ToFloat4( ModListCache.HandledConflictModColor ),
                "启用后与另一个已启用的模组存在冲突, 但不在相同的优先级 (例如手动解决了冲突)." );
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextColored( ImGui.ColorConvertU32ToFloat4( ModListCache.ConflictingModColor ),
                "启用后与另一个相同加载顺序的模组冲突." );
            ImGui.Unindent();
            ImGui.BulletText( "Right-click a mod to enter its sort order, which is its name by default." );
            ImGui.Indent();
            ImGui.BulletText( "A sort order differing from the mods name will not be displayed, it will just be used for ordering." );
            ImGui.BulletText(
                "If the sort order string contains Forward-Slashes ('/'), the preceding substring will be turned into collapsible folders that can group mods." );
            ImGui.BulletText(
                "Collapsible folders can contain further collapsible folders, so \"folder1/folder2/folder3/1\" will produce 3 folders\n"
              + "\t\t[folder1] -> [folder2] -> [folder3] -> [ModName],\n"
              + "where ModName will be sorted as if it was the string '1'." );
            ImGui.Unindent();
            ImGui.BulletText(
                "You can drag and drop mods and subfolders into existing folders. Dropping them onto mods is the same as dropping them onto the parent of the mod." );
            ImGui.BulletText( "右键单击一个文件夹会打开一个上下文菜单." );
            ImGui.Indent();
            ImGui.BulletText(
                "你可以在上下文菜单中重命名文件夹. 将文本留空, 并按回车键会将文件夹与其父文件夹合并." );
            ImGui.BulletText( "你还可以启用或禁用一个文件夹的所有子模组." );
            ImGui.Unindent();
            ImGui.BulletText( "使用 过滤模组... 的顶部输入框, 以过滤列表中名称包含文本的模组." );
            ImGui.Indent();
            ImGui.BulletText( "你可以输入 c:[字符串] 来过滤更换了特定物品的模组." );
            ImGui.BulletText( "你可以输入 a:[字符串] 来过滤特定作者的模组." );
            ImGui.Unindent();
            ImGui.BulletText( "使用输入框旁边的可扩展菜单来过滤符合特定条件的模组." );
            ImGui.Dummy( Vector2.UnitY * ImGui.GetTextLineHeight() );
            ImGui.Text( "模组管理" );
            ImGui.BulletText( "你可以点击\"垃圾桶\"按钮删除当前选中的模组." );
            ImGui.BulletText( "你可以使用\"加号\"来创建完全空白的模组." );
            ImGui.BulletText( "你可以在导入模组选项卡导入TexTools模组." );
            ImGui.BulletText(
                "你可以导入基于Penumbra的mod, 通过移动相应的文件夹到你的模组根目录中, 然后重新载入模组." );
            ImGui.BulletText(
                "如果你在设置选项卡中启用了高级选项, 你可以切换编辑模式来更进一步操作你选择的模组." );
            ImGui.Dummy( Vector2.UnitY * ImGui.GetTextLineHeight() );
            ImGui.Dummy( Vector2.UnitX * 2 * SelectorPanelWidth );
            ImGui.SameLine();
            if( ImGui.Button( "我了解了", Vector2.UnitX * SelectorPanelWidth ) )
            {
                ImGui.CloseCurrentPopup();
            }
        }

        // === Main ===
        private void DrawModsSelectorButtons()
        {
            // Selector controls
            using var style = ImGuiRaii.PushStyle( ImGuiStyleVar.WindowPadding, ZeroVector )
               .Push( ImGuiStyleVar.FrameRounding, 0 );

            DrawModAddButton();
            ImGui.SameLine();
            DrawModHelpButton();
            ImGui.SameLine();
            DrawModTrashButton();
        }
    }

    // Filters
    private partial class Selector
    {
        private string _modFilterInput = "";

        private void DrawTextFilter()
        {
            ImGui.SetNextItemWidth( SelectorPanelWidth * _selectorScalingFactor - 22 * ImGuiHelpers.GlobalScale );
            var tmp = _modFilterInput;
            if( ImGui.InputTextWithHint( LabelModFilter, "过滤模组...", ref tmp, 256 ) && _modFilterInput != tmp )
            {
                Cache.SetTextFilter( tmp );
                _modFilterInput = tmp;
            }

            ImGuiCustom.HoverTooltip( TooltipModFilter );
        }

        private void DrawToggleFilter()
        {
            if( ImGui.BeginCombo( "##ModStateFilter", "",
                   ImGuiComboFlags.NoPreview | ImGuiComboFlags.PopupAlignLeft | ImGuiComboFlags.HeightLargest ) )
            {
                using var raii  = ImGuiRaii.DeferredEnd( ImGui.EndCombo );
                var       flags = ( int )Cache.StateFilter;
                foreach( ModFilter flag in Enum.GetValues( typeof( ModFilter ) ) )
                {
                    ImGui.CheckboxFlags( flag.ToName(), ref flags, ( int )flag );
                }

                Cache.StateFilter = ( ModFilter )flags;
            }

            ImGuiCustom.HoverTooltip( "过滤模组的启用状态." );
        }

        private void DrawModsSelectorFilter()
        {
            using var style = ImGuiRaii.PushStyle( ImGuiStyleVar.ItemSpacing, ZeroVector );
            DrawTextFilter();
            ImGui.SameLine();
            DrawToggleFilter();
        }
    }

    // Drag'n Drop
    private partial class Selector
    {
        private const string DraggedModLabel    = "ModIndex";
        private const string DraggedFolderLabel = "FolderName";

        private readonly IntPtr _dragDropPayload = Marshal.AllocHGlobal( 4 );

        private static unsafe bool IsDropping( string name )
            => ImGui.AcceptDragDropPayload( name ).NativePtr != null;

        private void DragDropTarget( ModFolder folder )
        {
            if( !ImGui.BeginDragDropTarget() )
            {
                return;
            }

            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndDragDropTarget );

            if( IsDropping( DraggedModLabel ) )
            {
                var payload  = ImGui.GetDragDropPayload();
                var modIndex = Marshal.ReadInt32( payload.Data );
                var mod      = Cache.GetMod( modIndex ).Item1;
                mod?.Data.Move( folder );
            }
            else if( IsDropping( DraggedFolderLabel ) )
            {
                var payload    = ImGui.GetDragDropPayload();
                var folderName = Marshal.PtrToStringUni( payload.Data );
                if( ModFileSystem.Find( folderName!, out var droppedFolder )
                && !ReferenceEquals( droppedFolder, folder )
                && !folder.FullName.StartsWith( folderName!, StringComparison.InvariantCultureIgnoreCase ) )
                {
                    droppedFolder.Move( folder );
                }
            }
        }

        private void DragDropSourceFolder( ModFolder folder )
        {
            if( !ImGui.BeginDragDropSource() )
            {
                return;
            }

            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndDragDropSource );

            var folderName = folder.FullName;
            var ptr        = Marshal.StringToHGlobalUni( folderName );
            ImGui.SetDragDropPayload( DraggedFolderLabel, ptr, ( uint )( folderName.Length + 1 ) * 2 );
            ImGui.Text( $"Moving {folderName}..." );
        }

        private void DragDropSourceMod( int modIndex, string modName )
        {
            if( !ImGui.BeginDragDropSource() )
            {
                return;
            }

            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndDragDropSource );

            Marshal.WriteInt32( _dragDropPayload, modIndex );
            ImGui.SetDragDropPayload( "ModIndex", _dragDropPayload, 4 );
            ImGui.Text( $"Moving {modName}..." );
        }

        ~Selector()
            => Marshal.FreeHGlobal( _dragDropPayload );
    }

    // Selection
    private partial class Selector
    {
        public Mod.Mod? Mod { get; private set; }
        private int    _index;
        private string _nextDir = string.Empty;

        private void SetSelection( int idx, Mod.Mod? info )
        {
            Mod = info;
            if( idx != _index )
            {
                _base._menu.InstalledTab.ModPanel.Details.ResetState();
            }

            _index       = idx;
            _deleteIndex = null;
        }

        private void SetSelection( int idx )
        {
            if( idx >= Cache.Count )
            {
                idx = -1;
            }

            if( idx < 0 )
            {
                SetSelection( 0, null );
            }
            else
            {
                SetSelection( idx, Cache.GetMod( idx ).Item1 );
            }
        }

        public void ReloadSelection()
            => SetSelection( _index, Cache.GetMod( _index ).Item1 );

        public void ClearSelection()
            => SetSelection( -1 );

        public void SelectModOnUpdate( string directory )
            => _nextDir = directory;

        public void SelectModByDir( string name )
        {
            var (mod, idx) = Cache.GetModByBasePath( name );
            SetSelection( idx, mod );
        }

        public void ReloadCurrentMod( bool reloadMeta = false, bool recomputeMeta = false, bool force = false )
        {
            if( Mod == null )
            {
                return;
            }

            if( _index >= 0 && _modManager.UpdateMod( Mod.Data, reloadMeta, recomputeMeta, force ) )
            {
                SelectModOnUpdate( Mod.Data.BasePath.Name );
                _base._menu.InstalledTab.ModPanel.Details.ResetState();
            }
        }

        public void SaveCurrentMod()
            => Mod?.Data.SaveMeta();
    }

    // Right-Clicks
    private partial class Selector
    {
        // === Mod ===
        private void DrawModOrderPopup( string popupName, Mod.Mod mod, bool firstOpen )
        {
            if( !ImGui.BeginPopup( popupName ) )
            {
                return;
            }

            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndPopup );

            if( ModPanel.DrawSortOrder( mod.Data, _modManager, this ) )
            {
                ImGui.CloseCurrentPopup();
            }

            if( firstOpen )
            {
                ImGui.SetKeyboardFocusHere( mod.Data.SortOrder.FullPath.Length - 1 );
            }
        }

        // === Folder ===
        private string _newFolderName = string.Empty;
        private int    _expandIndex   = -1;
        private bool   _expandCollapse;
        private bool   _currentlyExpanding;

        private void ChangeStatusOfChildren( ModFolder folder, int currentIdx, bool toWhat )
        {
            var change     = false;
            var metaManips = false;
            foreach( var _ in folder.AllMods( _modManager.Config.SortFoldersFirst ) )
            {
                var (mod, _, _) = Cache.GetMod( currentIdx++ );
                if( mod != null )
                {
                    change                |= mod.Settings.Enabled != toWhat;
                    mod!.Settings.Enabled =  toWhat;
                    metaManips            |= mod.Data.Resources.MetaManipulations.Count > 0;
                }
            }

            if( !change )
            {
                return;
            }

            Cache.TriggerFilterReset();
            var collection = _modManager.Collections.CurrentCollection;
            if( collection.Cache != null )
            {
                collection.CalculateEffectiveFileList( _modManager.TempPath, metaManips,
                    collection == _modManager.Collections.ActiveCollection );
            }

            collection.Save();
        }

        private void DrawRenameFolderInput( ModFolder folder )
        {
            ImGui.SetNextItemWidth( 150 * ImGuiHelpers.GlobalScale );
            if( !ImGui.InputTextWithHint( "##NewFolderName", "Rename Folder...", ref _newFolderName, 64,
                   ImGuiInputTextFlags.EnterReturnsTrue ) )
            {
                return;
            }

            if( _newFolderName.Any() )
            {
                folder.Rename( _newFolderName );
            }
            else
            {
                folder.Merge( folder.Parent! );
            }

            _newFolderName = string.Empty;
        }

        private void DrawFolderContextMenu( ModFolder folder, int currentIdx, string treeName )
        {
            if( !ImGui.BeginPopup( treeName ) )
            {
                return;
            }

            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndPopup );

            if( ImGui.MenuItem( "Expand All Descendants" ) )
            {
                _expandIndex    = currentIdx;
                _expandCollapse = false;
            }

            if( ImGui.MenuItem( "Collapse All Descendants" ) )
            {
                _expandIndex    = currentIdx;
                _expandCollapse = true;
            }

            if( ImGui.MenuItem( "Enable All Descendants" ) )
            {
                ChangeStatusOfChildren( folder, currentIdx, true );
            }

            if( ImGui.MenuItem( "Disable All Descendants" ) )
            {
                ChangeStatusOfChildren( folder, currentIdx, false );
            }

            ImGuiHelpers.ScaledDummy( 0, 10 );
            DrawRenameFolderInput( folder );
        }
    }

    // Main-Interface
    private partial class Selector
    {
        private readonly SettingsInterface _base;
        private readonly ModManager        _modManager;
        public readonly  ModListCache      Cache;

        private float _selectorScalingFactor = 1;

        public Selector( SettingsInterface ui, IReadOnlySet< string > newMods )
        {
            _base       = ui;
            _modManager = Service< ModManager >.Get();
            Cache       = new ModListCache( _modManager, newMods );
        }

        private void DrawCollectionButton( string label, string tooltipLabel, float size, ModCollection collection )
        {
            if( collection == ModCollection.Empty
            || collection  == _modManager.Collections.CurrentCollection )
            {
                using var _ = ImGuiRaii.PushStyle( ImGuiStyleVar.Alpha, 0.5f );
                ImGui.Button( label, Vector2.UnitX * size );
            }
            else if( ImGui.Button( label, Vector2.UnitX * size ) )
            {
                _base._menu.CollectionsTab.SetCurrentCollection( collection );
            }

            ImGuiCustom.HoverTooltip(
                $"Switches to the currently set {tooltipLabel} collection, if it is not set to None and it is not the current collection already." );
        }

        private void DrawHeaderBar()
        {
            const float size = 200;

            DrawModsSelectorFilter();
            var textSize  = ImGui.CalcTextSize( "Current Collection" ).X + ImGui.GetStyle().ItemInnerSpacing.X;
            var comboSize = size * ImGui.GetIO().FontGlobalScale;
            var offset    = comboSize + textSize;

            var buttonSize = Math.Max( ( ImGui.GetWindowContentRegionWidth()
                  - offset
                  - SelectorPanelWidth * _selectorScalingFactor
                  - 4                  * ImGui.GetStyle().ItemSpacing.X )
              / 2, 5f );
            ImGui.SameLine();
            DrawCollectionButton( "Default", "default", buttonSize, _modManager.Collections.DefaultCollection );

            ImGui.SameLine();
            DrawCollectionButton( "Forced", "forced", buttonSize, _modManager.Collections.ForcedCollection );

            ImGui.SameLine();
            ImGui.SetNextItemWidth( comboSize );
            using var style = ImGuiRaii.PushStyle( ImGuiStyleVar.ItemSpacing, Vector2.Zero );
            _base._menu.CollectionsTab.DrawCurrentCollectionSelector( false );
        }

        private void DrawFolderContent( ModFolder folder, ref int idx )
        {
            // Collection may be manipulated.
            foreach( var item in folder.GetItems( _modManager.Config.SortFoldersFirst ).ToArray() )
            {
                if( item is ModFolder sub )
                {
                    var (visible, _) = Cache.GetFolder( sub );
                    if( visible )
                    {
                        DrawModFolder( sub, ref idx );
                    }
                    else
                    {
                        idx += sub.TotalDescendantMods();
                    }
                }
                else if( item is ModData _ )
                {
                    var (mod, visible, color) = Cache.GetMod( idx );
                    if( mod != null && visible )
                    {
                        DrawMod( mod, idx++, color );
                    }
                    else
                    {
                        ++idx;
                    }
                }
            }
        }

        private void DrawModFolder( ModFolder folder, ref int idx )
        {
            var       treeName = $"{folder.Name}##{folder.FullName}";
            var       open     = ImGui.TreeNodeEx( treeName );
            using var raii     = ImGuiRaii.DeferredEnd( ImGui.TreePop, open );

            if( idx == _expandIndex )
            {
                _currentlyExpanding = true;
            }

            if( _currentlyExpanding )
            {
                ImGui.SetNextItemOpen( !_expandCollapse );
            }

            if( ImGui.IsItemClicked( ImGuiMouseButton.Right ) )
            {
                _newFolderName = string.Empty;
                ImGui.OpenPopup( treeName );
            }

            DrawFolderContextMenu( folder, idx, treeName );
            DragDropTarget( folder );
            DragDropSourceFolder( folder );

            if( open )
            {
                DrawFolderContent( folder, ref idx );
            }
            else
            {
                idx += folder.TotalDescendantMods();
            }

            if( idx == _expandIndex )
            {
                _currentlyExpanding = false;
                _expandIndex        = -1;
            }
        }

        private void DrawMod( Mod.Mod mod, int modIndex, uint color )
        {
            using var colorRaii = ImGuiRaii.PushColor( ImGuiCol.Text, color, color != 0 );

            var selected = ImGui.Selectable( $"{mod.Data.Meta.Name}##{modIndex}", modIndex == _index );
            colorRaii.Pop();

            var popupName = $"##SortOrderPopup{modIndex}";
            var firstOpen = false;
            if( ImGui.IsItemClicked( ImGuiMouseButton.Right ) )
            {
                ImGui.OpenPopup( popupName );
                firstOpen = true;
            }

            DragDropTarget( mod.Data.SortOrder.ParentFolder );
            DragDropSourceMod( modIndex, mod.Data.Meta.Name );

            DrawModOrderPopup( popupName, mod, firstOpen );

            if( selected )
            {
                SetSelection( modIndex, mod );
            }
        }

        public void Draw()
        {
            if( Cache.Update() )
            {
                if( _nextDir.Any() )
                {
                    SelectModByDir( _nextDir );
                    _nextDir = string.Empty;
                }
                else if( Mod != null )
                {
                    SelectModByDir( Mod.Data.BasePath.Name );
                }
            }

            _selectorScalingFactor = ImGuiHelpers.GlobalScale
              * ( Penumbra.Config.ScaleModSelector
                    ? ImGui.GetWindowWidth() / SettingsMenu.MinSettingsSize.X
                    : 1f );
            // Selector pane
            DrawHeaderBar();
            using var style = ImGuiRaii.PushStyle( ImGuiStyleVar.ItemSpacing, Vector2.Zero );
            ImGui.BeginGroup();
            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndGroup )
               .Push( ImGui.EndChild );
            // Inlay selector list
            if( ImGui.BeginChild( LabelSelectorList,
                   new Vector2( SelectorPanelWidth * _selectorScalingFactor, -ImGui.GetFrameHeightWithSpacing() ),
                   true, ImGuiWindowFlags.HorizontalScrollbar ) )
            {
                style.Push( ImGuiStyleVar.IndentSpacing, 12.5f );

                var modIndex = 0;
                DrawFolderContent( _modManager.StructuredMods, ref modIndex );
                style.Pop();
            }

            raii.Pop();

            DrawModsSelectorButtons();

            style.Pop();
            DrawModHelpPopup();

            DrawDeleteModal();
        }
    }
}