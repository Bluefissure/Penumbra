using ImGuiNET;
using Penumbra.UI.Custom;

namespace Penumbra.UI
{
    public partial class SettingsInterface
    {
        private class MenuBar
        {
            private const string MenuLabel          = "Penumbra";
            private const string MenuItemToggle     = "打开GUI";
            private const string SlashCommand       = "/penumbra";
            private const string MenuItemRediscover = "重载模组";
            private const string MenuItemHide       = "隐藏菜单栏";

#if DEBUG
            private bool _showDebugBar = true;
#else
            private const bool _showDebugBar = false;
#endif

            private readonly SettingsInterface _base;

            public MenuBar( SettingsInterface ui )
                => _base = ui;

            public void Draw()
            {
                if( !_showDebugBar || !ImGui.BeginMainMenuBar() )
                {
                    return;
                }

                using var raii = ImGuiRaii.DeferredEnd( ImGui.EndMainMenuBar );

                if( !ImGui.BeginMenu( MenuLabel ) )
                {
                    return;
                }

                raii.Push( ImGui.EndMenu );

                if( ImGui.MenuItem( MenuItemToggle, SlashCommand, _base._menu.Visible ) )
                {
                    _base.FlipVisibility();
                }

                if( ImGui.MenuItem( MenuItemRediscover ) )
                {
                    _base.ReloadMods();
                }
#if DEBUG
                if( ImGui.MenuItem( MenuItemHide ) )
                {
                    _showDebugBar = false;
                }
#endif
            }
        }
    }
}