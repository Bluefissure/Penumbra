using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Logging;
using ImGuiNET;
using Penumbra.Mod;
using Penumbra.Mods;
using Penumbra.UI.Custom;
using Penumbra.Util;

namespace Penumbra.UI;

public partial class SettingsInterface
{
    private class TabCollections
    {
        private const    string                    CharacterCollectionHelpPopup = "角色合集信息";
        private readonly Selector                  _selector;
        private readonly ModManager                _manager;
        private          string                    _collectionNames         = null!;
        private          string                    _collectionNamesWithNone = null!;
        private          ModCollection[]           _collections             = null!;
        private          int                       _currentCollectionIndex;
        private          int                       _currentForcedIndex;
        private          int                       _currentDefaultIndex;
        private readonly Dictionary< string, int > _currentCharacterIndices = new();
        private          string                    _newCollectionName       = string.Empty;
        private          string                    _newCharacterName        = string.Empty;

        private void UpdateNames()
        {
            _collections             = _manager.Collections.Collections.Values.Prepend( ModCollection.Empty ).ToArray();
            _collectionNames         = string.Join( "\0", _collections.Skip( 1 ).Select( c => c.Name ) ) + '\0';
            _collectionNamesWithNone = "None\0"                                                          + _collectionNames;
            UpdateIndices();
        }


        private int GetIndex( ModCollection collection )
        {
            var ret = _collections.IndexOf( c => c.Name == collection.Name );
            if( ret < 0 )
            {
                PluginLog.Error( $"合集 {collection.Name} 未能在合集列表中找到." );
                return 0;
            }

            return ret;
        }

        private void UpdateIndex()
            => _currentCollectionIndex = GetIndex( _manager.Collections.CurrentCollection ) - 1;

        public void UpdateForcedIndex()
            => _currentForcedIndex = GetIndex( _manager.Collections.ForcedCollection );

        public void UpdateDefaultIndex()
            => _currentDefaultIndex = GetIndex( _manager.Collections.DefaultCollection );

        private void UpdateCharacterIndices()
        {
            _currentCharacterIndices.Clear();
            foreach( var kvp in _manager.Collections.CharacterCollection )
            {
                _currentCharacterIndices[ kvp.Key ] = GetIndex( kvp.Value );
            }
        }

        private void UpdateIndices()
        {
            UpdateIndex();
            UpdateDefaultIndex();
            UpdateForcedIndex();
            UpdateCharacterIndices();
        }

        public TabCollections( Selector selector )
        {
            _selector = selector;
            _manager  = Service< ModManager >.Get();
            UpdateNames();
        }

        private void CreateNewCollection( Dictionary< string, ModSettings > settings )
        {
            if( _manager.Collections.AddCollection( _newCollectionName, settings ) )
            {
                UpdateNames();
                SetCurrentCollection( _manager.Collections.Collections[ _newCollectionName ], true );
            }

            _newCollectionName = string.Empty;
        }

        private void DrawCleanCollectionButton()
        {
            if( ImGui.Button( "清除设置" ) )
            {
                var changes = ModFunctions.CleanUpCollection( _manager.Collections.CurrentCollection.Settings,
                    _manager.BasePath.EnumerateDirectories() );
                _manager.Collections.CurrentCollection.UpdateSettings( changes );
            }

            ImGuiCustom.HoverTooltip(
                "删除当前不可用的模组的所有存储设置, 并修复无效设置.\n请谨慎对待." );
        }

        private void DrawNewCollectionInput()
        {
            ImGui.SetNextItemWidth( SettingsMenu.InputTextWidth );
            ImGui.InputTextWithHint( "##New Collection", "新合集名称", ref _newCollectionName, 64 );
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "合集是用于存储安装的mod的设置, 包括它们的启用状态、优先级和特定于模组的配置.\n"
              + "你可以使用多个合集来快速切换模组设定." );

            using var style = ImGuiRaii.PushStyle( ImGuiStyleVar.Alpha, 0.5f, _newCollectionName.Length == 0 );

            if( ImGui.Button( "创建空白合集" ) && _newCollectionName.Length > 0 )
            {
                CreateNewCollection( new Dictionary< string, ModSettings >() );
            }

            var hover = ImGui.IsItemHovered();
            ImGui.SameLine();
            if( ImGui.Button( "复制当前合集" ) && _newCollectionName.Length > 0 )
            {
                CreateNewCollection( _manager.Collections.CurrentCollection.Settings );
            }

            hover |= ImGui.IsItemHovered();

            style.Pop();
            if( _newCollectionName.Length == 0 && hover )
            {
                ImGui.SetTooltip( "请在创建合集前设置一个名字." );
            }

            var deleteCondition = _manager.Collections.Collections.Count > 1
             && _manager.Collections.CurrentCollection.Name              != ModCollection.DefaultCollection;
            ImGui.SameLine();
            if( ImGuiCustom.DisableButton( "删除当前合集", deleteCondition ) )
            {
                _manager.Collections.RemoveCollection( _manager.Collections.CurrentCollection.Name );
                SetCurrentCollection( _manager.Collections.CurrentCollection, true );
                UpdateNames();
            }

            if( !deleteCondition )
            {
                ImGuiCustom.HoverTooltip( "你无法删除默认合集." );
            }

            if( Penumbra.Config.ShowAdvanced )
            {
                ImGui.SameLine();
                DrawCleanCollectionButton();
            }
        }

        private void SetCurrentCollection( int idx, bool force )
        {
            if( !force && idx == _currentCollectionIndex )
            {
                return;
            }

            _manager.Collections.SetCurrentCollection( _collections[ idx + 1 ] );
            _currentCollectionIndex = idx;
            _selector.Cache.TriggerListReset();
            if( _selector.Mod != null )
            {
                _selector.SelectModOnUpdate( _selector.Mod.Data.BasePath.Name );
            }
        }

        public void SetCurrentCollection( ModCollection collection, bool force = false )
        {
            var idx = Array.IndexOf( _collections, collection ) - 1;
            if( idx >= 0 )
            {
                SetCurrentCollection( idx, force );
            }
        }

        public void DrawCurrentCollectionSelector( bool tooltip )
        {
            var index = _currentCollectionIndex;
            ImGui.SetNextItemWidth( SettingsMenu.InputTextWidth );
            var combo = ImGui.Combo( "当前合集", ref index, _collectionNames );
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "当使用已安装模组选项卡并做出改动时, 此合集会自动更改. 它本身并没啥用." );

            if( combo )
            {
                SetCurrentCollection( index, false );
            }
        }

        private void DrawDefaultCollectionSelector()
        {
            var index = _currentDefaultIndex;
            ImGui.SetNextItemWidth( SettingsMenu.InputTextWidth );
            if( ImGui.Combo( "##Default Collection", ref index, _collectionNamesWithNone ) && index != _currentDefaultIndex )
            {
                _manager.Collections.SetDefaultCollection( _collections[ index ] );
                _currentDefaultIndex = index;
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "默认合集中的模组会被加载到任何没有在下面的角色合集中显式命名的角色.\n"
              + "它们也优先于强制合集." );

            ImGui.SameLine();
            ImGui.Text( "默认合集" );
        }

        private void DrawForcedCollectionSelector()
        {
            var index = _currentForcedIndex;
            ImGui.SetNextItemWidth( SettingsMenu.InputTextWidth );
            using var style = ImGuiRaii.PushStyle( ImGuiStyleVar.Alpha, 0.5f, _manager.Collections.CharacterCollection.Count == 0 );
            if( ImGui.Combo( "##Forced Collection", ref index, _collectionNamesWithNone )
            && index                                          != _currentForcedIndex
            && _manager.Collections.CharacterCollection.Count > 0 )
            {
                _manager.Collections.SetForcedCollection( _collections[ index ] );
                _currentForcedIndex = index;
            }

            style.Pop();
            if( _manager.Collections.CharacterCollection.Count == 0 && ImGui.IsItemHovered() )
            {
                ImGui.SetTooltip(
                    "强制合集只在至少有一个角色合集的情况下才会提供值. 在此之前不需要设置." );
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "强制合集中的模组如果没有被当前或基于角色的合集中的任何东西覆盖, 则总是被加载.\n"
              + "请避免在强制和其他合集中混合使用模组, 因为这可能无法正常工作." );
            ImGui.SameLine();
            ImGui.Text( "强制合集" );
        }

        private void DrawNewCharacterCollection()
        {
            ImGui.SetNextItemWidth( SettingsMenu.InputTextWidth );
            ImGui.InputTextWithHint( "##New Character", "新角色名称", ref _newCharacterName, 32 );
            ImGui.SameLine();
            ImGuiComponents.HelpMarker( "点我来查看帮助文本!" );
            ImGui.OpenPopupOnItemClick( CharacterCollectionHelpPopup, ImGuiPopupFlags.MouseButtonLeft );

            ImGui.SameLine();
            if( ImGuiCustom.DisableButton( "创建新角色合集",
                   _newCharacterName.Length > 0 && Penumbra.Config.HasReadCharacterCollectionDesc ) )
            {
                _manager.Collections.CreateCharacterCollection( _newCharacterName );
                _currentCharacterIndices[ _newCharacterName ] = 0;
                _newCharacterName                             = string.Empty;
            }

            ImGuiCustom.HoverTooltip( "在创建合集之前, 请输入角色名称.\n"
              + "你还需要阅读角色合集的帮助文本." );

            DrawCharacterCollectionHelp();
        }

        private static void DrawCharacterCollectionHelp()
        {
            var size = new Vector2( 700 * ImGuiHelpers.GlobalScale, 34 * ImGui.GetTextLineHeightWithSpacing() );
            ImGui.SetNextWindowPos( ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, Vector2.One / 2 );
            ImGui.SetNextWindowSize( size, ImGuiCond.Appearing );
            var _ = true;
            if( ImGui.BeginPopupModal( CharacterCollectionHelpPopup, ref _, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove ) )
            {
                const string header    = "角色合集是一种外挂行为! 请自行承担风险.";
                using var    end       = ImGuiRaii.DeferredEnd( ImGui.EndPopup );
                var          textWidth = ImGui.CalcTextSize( header ).X;
                ImGui.NewLine();
                ImGui.SetCursorPosX( ( size.X - textWidth ) / 2 );
                using var color = ImGuiRaii.PushColor( ImGuiCol.Text, 0xFF0000B8 );
                ImGui.Text( header );
                color.Pop();
                ImGui.NewLine();
                ImGui.TextWrapped(
                    "Character Collections are collections that get applied whenever the named character gets redrawn by Penumbra,"
                  + " whether by a manual '/penumbra redraw' command, or by the automatic redrawing feature.\n"
                  + "This means that they specifically require redrawing of a character to even apply, and thus can not work with mods that modify something that does not depend on characters being drawn, such as:\n"
                  + "        - animations\n"
                  + "        - sounds\n"
                  + "        - most effects\n"
                  + "        - most ui elements.\n"
                  + "They can also not work with actors that are not named, like the Character Preview or TryOn Actors, and they can not work in cutscenes, since redrawing in cutscenes would cancel all animations.\n"
                  + "They also do not work with every character customization (like skin, tattoo, hair, etc. changes) since those are not always re-requested by the game on redrawing a player. They may work, they may not, you need to test it.\n"
                  + "\n"
                  + "Due to the nature of meta manipulating mods, you can not mix meta manipulations inside a Character (or the Default) collection with meta manipulations inside the Forced collection.\n"
                  + "\n"
                  + "To verify that you have actually read this, you need to hold control and shift while clicking the Understood button for it to take effect.\n"
                  + "Due to the nature of redrawing being a hack, weird things (or maybe even crashes) may happen when using Character Collections. The way this works is:\n"
                  + "        - Penumbra queues a redraw of an actor.\n"
                  + "        - When the redraw queue reaches that actor, the actor gets undrawn (turned invisible).\n"
                  + "        - Penumbra checks the actors name and if it matches a Character Collection, it replaces the Default collection with that one.\n"
                  + "        - Penumbra triggers the redraw of that actor. The game requests files.\n"
                  + "        - Penumbra potentially redirects those file requests to the modded files in the active collection, which is either Default or Character. (Or, afterwards, Forced).\n"
                  + "        - The actor is drawn.\n"
                  + "        - Penumbra returns the active collection to the Default Collection.\n"
                  + "If any of those steps fails, or if the file requests take too long, it may happen that a character is drawn with half of its models from the Default and the other half from the Character Collection, or a modded Model is loaded, but not its corresponding modded textures, which lets it stay invisible, or similar problems." );

                var buttonSize = ImGuiHelpers.ScaledVector2( 150, 0 );
                var offset     = ( size.X - buttonSize.X ) / 2;
                ImGui.SetCursorPos( new Vector2( offset, size.Y - 3 * ImGui.GetTextLineHeightWithSpacing() ) );
                var state = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
                color.Push( ImGuiCol.ButtonHovered, 0xFF00A000, state );
                if( ImGui.Button( "我了解了!", buttonSize ) )
                {
                    if( state && !Penumbra.Config.HasReadCharacterCollectionDesc )
                    {
                        Penumbra.Config.HasReadCharacterCollectionDesc = true;
                        Penumbra.Config.Save();
                    }

                    ImGui.CloseCurrentPopup();
                }
            }
        }


        private void DrawCharacterCollectionSelectors()
        {
            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndChild );
            if( !ImGui.BeginChild( "##CollectionChild", AutoFillSize, true ) )
            {
                return;
            }

            DrawDefaultCollectionSelector();
            DrawForcedCollectionSelector();

            foreach( var name in _manager.Collections.CharacterCollection.Keys.ToArray() )
            {
                var idx = _currentCharacterIndices[ name ];
                var tmp = idx;
                ImGui.SetNextItemWidth( SettingsMenu.InputTextWidth );
                if( ImGui.Combo( $"##{name}collection", ref tmp, _collectionNamesWithNone ) && idx != tmp )
                {
                    _manager.Collections.SetCharacterCollection( name, _collections[ tmp ] );
                    _currentCharacterIndices[ name ] = tmp;
                }

                ImGui.SameLine();

                using var font = ImGuiRaii.PushFont( UiBuilder.IconFont );

                using var style = ImGuiRaii.PushStyle( ImGuiStyleVar.FramePadding, Vector2.One * ImGuiHelpers.GlobalScale * 1.5f );
                if( ImGui.Button( $"{FontAwesomeIcon.Trash.ToIconString()}##{name}" ) )
                {
                    _manager.Collections.RemoveCharacterCollection( name );
                }

                style.Pop();

                font.Pop();

                ImGui.SameLine();
                ImGui.Text( name );
            }

            DrawNewCharacterCollection();
        }

        public void Draw()
        {
            if( !ImGui.BeginTabItem( "合集" ) )
            {
                return;
            }

            using var raii = ImGuiRaii.DeferredEnd( ImGui.EndTabItem )
               .Push( ImGui.EndChild );

            if( ImGui.BeginChild( "##CollectionHandling", new Vector2( -1, ImGui.GetTextLineHeightWithSpacing() * 6 ), true ) )
            {
                DrawCurrentCollectionSelector( true );

                ImGuiHelpers.ScaledDummy( 0, 10 );
                DrawNewCollectionInput();
            }

            raii.Pop();

            DrawCharacterCollectionSelectors();
        }
    }
}