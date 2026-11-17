# Host migration requirements

Understand the requirements for using host migration in a project and which platforms are supported.

After ensuring your project meets these requirements, you can move on to [setting up host migration systems in your project](host-migration-systems.md).

## Requirements

Before you can use host migration in a project, you need the following:

- An active Unity account with a valid license.
- The Unity Hub.
- A supported version of the Unity 6 Editor.
- Access to the Unity Cloud Dashboard.

## Unity project setup

You can start a new Unity project, or use the [Asteroids sample](host-migration-sample.md) to quickly get started testing host migration in Netcode for Entities. When you create a new project, connect the project to Unity Cloud by selecting the **Connect to Unity Cloud** checkbox.

## Packages

- Netcode for Entities (com.unity.netcode): 1.5.0-exp.100 or newer
- Multiplayer Services SDK (com.unity.services.multiplayer): 1.2.0-exp.2 or newer

## Disable auto connect

Host migration requires your project to manage connections itself, so you must disable auto connect. Netcode for Entities enables auto connect by default, with `ClientServerBootstrap.AutoConnectPort` set to `7979`.

With auto connect enabled, each instance commits to a fixed role and endpoint when it creates its network driver. Depending on the play type, it listens on the auto connect port, connects to `ClientServerBootstrap.DefaultConnectAddress`, or does both. Host migration has to make that choice later instead. The Lobby service elects the host at runtime, and there's no Relay allocation until a player creates or joins a session.

To disable auto connect, set `AutoConnectPort` to `0` in a [custom bootstrap](../client-server-worlds.md#customize-the-bootstrapping-flow) before you create any worlds:

```c#
// Preserve stops managed stripping from removing the bootstrap in IL2CPP builds.
[UnityEngine.Scripting.Preserve]
public class HostMigrationBootstrap : ClientServerBootstrap
{
    public override bool Initialize(string defaultWorldName)
    {
        // Host migration manages connections itself, so disable auto connect.
        AutoConnectPort = 0;

        // Create only a local world here. Create the client and server worlds later,
        // once the player creates or joins a session and you know who hosts it.
        CreateLocalWorld(defaultWorldName);
        return true;
    }
}
```

Alternatively, disable automatic bootstrapping in your `NetcodeConfig` asset (**Edit** > **Project Settings** > **Multiplayer**) and create the worlds yourself.

For more information about the available connection options, refer to [Connecting server and clients](../network-connection.md).

## Services and costs

Host migration coordination and state transfer is a feature provided by the [Unity Lobby](https://docs.unity.com/ugs/en-us/manual/lobby/manual/unity-lobby-service) service. The feature is included at no additional charge. Upload and download bandwidth for host migration data is not billable and does not count towards the free tier allowance or paid tier.

The [Unity Relay](https://docs.unity.com/ugs/en-us/manual/relay/manual/introduction) service works in conjunction with the Lobby service and guarantees reliable connectivity by relaying the messages between all parties in a game session. Relay pricing is based on connection time and egress bandwidth. The Free tier allows up to 50 average monthly CCUs and 3GiB of bandwidth per concurrent user.

Visit the [Unity Gaming Services Pricing page](https://unity.com/products/gaming-services/pricing) for details.

## Supported platforms

* Desktop: Windows, macOS, Linux
* Mobile: Android, iOS
* Console: Nintendo Switch, Xbox, Playstation 4, Playstation 5
* Dedicated Server: Linux, Window, MacOS
* Web: WebGL

## Additional resources

* [Introduction to host migration](host-migration-intro.md)
* [Limitations and known issues](host-migration-limitations.md)
* [Host migration in Asteroids sample](host-migration-sample.md)
* [Unity Lobby documentation](https://docs.unity.com/ugs/en-us/manual/lobby/manual/unity-lobby-service)
* [Unity Relay documentation](https://docs.unity.com/ugs/en-us/manual/relay/manual/introduction)
