﻿using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Serilog;
using Vint.Core.ECS.Components;
using Vint.Core.ECS.Entities;
using Vint.Core.Server.Game.Protocol.Attributes;
using Vint.Core.Utils;

namespace Vint.Core.Server.Game.Protocol.Commands;

[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Local")]
public class ComponentChangeCommand : IServerCommand {
    [ProtocolPosition(0)] public required IEntity Entity { get; init; }
    [ProtocolVaried, ProtocolPosition(1)] public required IComponent Component { get; init; }

    public async Task Execute(IPlayerConnection connection, IServiceProvider serviceProvider) {
        ILogger logger = connection.Logger.ForType<ComponentChangeCommand>();
        Type type = Component.GetType();
        ClientChangeableAttribute? clientChangeable = type.GetCustomAttribute<ClientChangeableAttribute>();

        if (clientChangeable == null) {
            logger.Error("{Component} is not in whitelist ({Entity})", type.Name, Entity);
            /*ChatUtils.SendMessage($"ClientChangeable: {type.Name}", ChatUtils.GetChat(connection), [connection], null);*/
            return; // maybe disconnect
        }

        await Entity.ChangeComponent(Component, connection);
        await Component.Changed(connection, Entity);

        logger.Debug("Changed {Component} in {Entity}", type.Name, Entity);
    }

    public override string ToString() =>
        $"ComponentChange command {{ Entity: {Entity}, Component: {Component.GetType().Name} }}";
}
