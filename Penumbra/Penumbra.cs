using System;
using Dalamud.Game.Command;
using Dalamud.Logging;
using Dalamud.Plugin;
using EmbedIO;
using EmbedIO.WebApi;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Penumbra.Api;
using Penumbra.GameData.Enums;
using Penumbra.Interop;
using Penumbra.Meta.Files;
using Penumbra.Mods;
using Penumbra.PlayerWatch;
using Penumbra.UI;
using Penumbra.Util;
using System.Linq;

namespace Penumbra;

public class Penumbra : IDalamudPlugin
{
    public string Name { get; } = "Penumbra";
    public string PluginDebugTitleStr { get; } = "Penumbra - Debug Build";

    private const string CommandName = "/penumbra";

    public static Configuration Config { get; private set; } = null!;
    public static IPlayerWatcher PlayerWatcher { get; private set; } = null!;

    public ResourceLoader ResourceLoader { get; }
    public SettingsInterface SettingsInterface { get; }
    public MusicManager MusicManager { get; }
    public ObjectReloader ObjectReloader { get; }

    public PenumbraApi Api { get; }
    public PenumbraIpc Ipc { get; }

    private WebServer? _webServer;

    private readonly ModManager _modManager;

    public Penumbra( DalamudPluginInterface pluginInterface )
    {
        Dalamud.Initialize( pluginInterface );
        GameData.GameData.GetIdentifier( Dalamud.GameData, Dalamud.ClientState.ClientLanguage );
        Config = Configuration.Load();

        MusicManager = new MusicManager();
        if( Config.DisableSoundStreaming )
        {
            MusicManager.DisableStreaming();
        }

        var gameUtils = Service< ResidentResources >.Set();
        PlayerWatcher = PlayerWatchFactory.Create( Dalamud.Framework, Dalamud.ClientState, Dalamud.Objects );
        Service< MetaDefaults >.Set();
        _modManager = Service< ModManager >.Set();

        _modManager.DiscoverMods();

        ObjectReloader = new ObjectReloader( _modManager, Config.WaitFrames );

        ResourceLoader = new ResourceLoader( this );

        Dalamud.Commands.AddHandler( CommandName, new CommandInfo( OnCommand )
        {
            HelpMessage = "/penumbra - 打开菜单\n/penumbra reload - 重载所有模组 & 搜寻新模组",
        } );

        ResourceLoader.Init();
        ResourceLoader.Enable();

        gameUtils.ReloadResidentResources();

        Api = new PenumbraApi( this );
        Ipc = new PenumbraIpc( pluginInterface, Api );
        SubscribeItemLinks();

        SettingsInterface = new SettingsInterface( this );

        if( Config.EnableHttpApi )
        {
            CreateWebServer();
        }

        if( !Config.EnablePlayerWatch || !Config.IsEnabled )
        {
            PlayerWatcher.Disable();
        }

        PlayerWatcher.PlayerChanged += p =>
        {
            PluginLog.Debug( "Triggered Redraw of {Player}.", p.Name );
            ObjectReloader.RedrawObject( p, RedrawType.OnlyWithSettings );
        };
    }

    public bool Enable()
    {
        if( Config.IsEnabled )
        {
            return false;
        }

        Config.IsEnabled = true;
        Service< ResidentResources >.Get().ReloadResidentResources();
        if( Config.EnablePlayerWatch )
        {
            PlayerWatcher.SetStatus( true );
        }

        Config.Save();
        ObjectReloader.RedrawAll( RedrawType.WithSettings );
        return true;
    }

    public bool Disable()
    {
        if( !Config.IsEnabled )
        {
            return false;
        }

        Config.IsEnabled = false;
        Service< ResidentResources >.Get().ReloadResidentResources();
        if( Config.EnablePlayerWatch )
        {
            PlayerWatcher.SetStatus( false );
        }

        Config.Save();
        ObjectReloader.RedrawAll( RedrawType.WithoutSettings );
        return true;
    }

    public bool SetEnabled( bool enabled )
        => enabled ? Enable() : Disable();

    private void SubscribeItemLinks()
    {
        Api.ChangedItemTooltip += it =>
        {
            if( it is Item )
            {
                ImGui.Text( "左键单击在聊天中创建一个物品展示." );
            }
        };
        Api.ChangedItemClicked += ( button, it ) =>
        {
            if( button == MouseButton.Left && it is Item item )
            {
                ChatUtil.LinkItem( item );
            }
        };
    }

    public void CreateWebServer()
    {
        const string prefix = "http://localhost:42069/";

        ShutdownWebServer();

        _webServer = new WebServer( o => o
               .WithUrlPrefix( prefix )
               .WithMode( HttpListenerMode.EmbedIO ) )
           .WithCors( prefix )
           .WithWebApi( "/api", m => m
               .WithController( () => new ModsController( this ) ) );

        _webServer.StateChanged += ( _, e ) => PluginLog.Information( $"WebServer New State - {e.NewState}" );

        _webServer.RunAsync();
    }

    public void ShutdownWebServer()
    {
        _webServer?.Dispose();
        _webServer = null;
    }

    public void Dispose()
    {
        Ipc.Dispose();
        Api.Dispose();
        SettingsInterface.Dispose();
        ObjectReloader.Dispose();
        PlayerWatcher.Dispose();

        Dalamud.Commands.RemoveHandler( CommandName );

        ResourceLoader.Dispose();

        ShutdownWebServer();
    }

    public bool SetCollection( string type, string collectionName )
    {
        type           = type.ToLowerInvariant();
        collectionName = collectionName.ToLowerInvariant();

        var collection = string.Equals( collectionName, ModCollection.Empty.Name, StringComparison.InvariantCultureIgnoreCase )
            ? ModCollection.Empty
            : _modManager.Collections.Collections.Values.FirstOrDefault( c
                => string.Equals( c.Name, collectionName, StringComparison.InvariantCultureIgnoreCase ) );
        if( collection == null )
        {
            Dalamud.Chat.Print( $"合集 {collection} 不存在." );
            return false;
        }

        switch( type )
        {
            case "default":
                if( collection == _modManager.Collections.DefaultCollection )
                {
                    Dalamud.Chat.Print( $"{collection.Name} 已经是默认合集." );
                    return false;
                }

                _modManager.Collections.SetDefaultCollection( collection );
                Dalamud.Chat.Print( $"设置 {collection.Name} 为默认合集." );
                SettingsInterface.ResetDefaultCollection();
                return true;
            case "forced":
                if( collection == _modManager.Collections.ForcedCollection )
                {
                    Dalamud.Chat.Print( $"{collection.Name} 已经是强制合集." );
                    return false;
                }

                _modManager.Collections.SetForcedCollection( collection );
                Dalamud.Chat.Print( $"设置 {collection.Name} 为强制合集." );
                SettingsInterface.ResetForcedCollection();
                return true;
            default:
                Dalamud.Chat.Print(
                    "第二个命令参数不是default或forced, 正确命令为: /penumbra collection {default|forced} <合集名称>" );
                return false;
        }
    }

    private void OnCommand( string command, string rawArgs )
    {
        const string modsEnabled  = "你的模组已启用.";
        const string modsDisabled = "你的模组已禁用.";

        var args = rawArgs.Split( new[] { ' ' }, 2 );
        if( args.Length > 0 && args[ 0 ].Length > 0 )
        {
            switch( args[ 0 ] )
            {
                case "reload":
                {
                    Service< ModManager >.Get().DiscoverMods();
                    Dalamud.Chat.Print(
                        $"已重载模组. 目前安装了 {_modManager.Mods.Count} 个模组."
                    );
                    break;
                }
                case "redraw":
                {
                    if( args.Length > 1 )
                    {
                        ObjectReloader.RedrawObject( args[ 1 ] );
                    }
                    else
                    {
                        ObjectReloader.RedrawAll();
                    }

                    break;
                }
                case "debug":
                {
                    SettingsInterface.MakeDebugTabVisible();
                    break;
                }
                case "enable":
                {
                    Dalamud.Chat.Print( Enable()
                        ? "你的模组已经启用. 若要禁用, 请输入如下指令: /penumbra disable"
                        : modsEnabled );
                    break;
                }
                case "disable":
                {
                    Dalamud.Chat.Print( Disable()
                        ? "你的模组已经禁用. 若要启用, 请输入如下指令: /penumbra enable"
                        : modsDisabled );
                    break;
                }
                case "toggle":
                {
                    SetEnabled( !Config.IsEnabled );
                    Dalamud.Chat.Print( Config.IsEnabled
                        ? modsEnabled
                        : modsDisabled );
                    break;
                }
                case "collection":
                {
                    if( args.Length == 2 )
                    {
                        args = args[ 1 ].Split( new[] { ' ' }, 2 );
                        if( args.Length == 2 )
                        {
                            SetCollection( args[ 0 ], args[ 1 ] );
                        }
                    }
                    else
                    {
                        Dalamud.Chat.Print( "丢失参数, 正确指令格式为:"
                          + " /penumbra collection {default|forced} <合集名>" );
                    }

                    break;
                }
            }

            return;
        }

        SettingsInterface.FlipVisibility();
    }
}