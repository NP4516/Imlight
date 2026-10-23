# Plugins

Plugins change how an Imlight server behaves without rebuilding Imlight. Drop a compiled plugin DLL into the `plugins` folder next to the server binary, and the server loads it on boot.

Plugins are event based: the server fires **events** as things happen, and plugins **subscribe** to them to react or to cancel the action. If you have ever developed plugins for Minecraft (Bukkit/Spigot, etc.) you will find this familiar.

## Writing a Plugin

A plugin is a .NET class library that references only `Imlight.PluginApi`. See [`ExampleImlightPlugin`](https://github.com/phazejeff/ExampleImlightPlugin) for a complete project.

```xml
<ItemGroup>
  <!-- The server provides Imlight.PluginApi at runtime; don't ship a copy. -->
  <ProjectReference Include="path/to/Imlight.PluginApi.csproj">
    <Private>false</Private>
  </ProjectReference>
</ItemGroup>
```

::: warning A plugin must be a single DLL
The server does not load a plugin's own dependencies. Anything a plugin needs beyond `Imlight.PluginApi` and the .NET runtime has to be compiled into the plugin DLL itself.
:::

Every non-abstract `ImlightPlugin` subclass in the DLL is created and enabled. Each one needs a public parameterless constructor:

```csharp
public sealed class GreeterPlugin : ImlightPlugin {

    public override string Name => "Greeter";

    public override string Version => "1.0.0";

    public override void OnEnable()
        => Events.Subscribe<PlayerZoneEnterEvent>(e => e.Player.SendMessage($"Welcome to {e.Zone}!"));

}
```

`OnEnable` runs at boot, before the servers accept connections. `OnDisable` runs when the process exits.

## Events

| Event | Cancellable | Fired |
| --- | --- | --- |
| `PlayerZoneEnterEvent` | No | When a player is added to a zone: on first login to a wizard, and after every zone change. |
| `PlayerChatEvent` | Yes | When a player sends zone chat, before it is logged and broadcast. Cancelling drops the message. Not fired for muted players, chat commands, whispers, group chat or quick chat. |

Handlers are matched on the event's exact type, so subscribing to `ImlightEvent` or another base type receives nothing. Handlers for the same event run in the order they subscribed.

::: danger Handlers run on the actor that fired the event
Events are delivered synchronously, on the thread of the actor that raised them. This lets a handler cancel an action before it happens, but a slow handler stalls that player's session. Return quickly, and never block.
:::

A handler that throws is logged and skipped. An `IPlayer` belongs to a single game session, and every zone change creates a new one, so store `CharacterId` rather than the `IPlayer`.

## Adding an Event

1. Add a class deriving from `ImlightEvent` to `Imlight.PluginApi/Events`. Implement `ICancellable` if handlers should be able to stop the action.
2. Fire it where the action happens, and respect cancellation if it applies:

```csharp
var player = new PluginPlayer(wizard, SessionActor.ActorRef);
if (PluginManager.Events.Fire(new PlayerChatEvent(player, message)).IsCancelled) {
    return;
}
```

3. Add the event to the [Events](#events) table above.
