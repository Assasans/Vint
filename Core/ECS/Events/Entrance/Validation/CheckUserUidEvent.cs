﻿using LinqToDB;
using Vint.Core.Database;
using Vint.Core.ECS.Entities;
using Vint.Core.Server.Game;
using Vint.Core.Server.Game.Protocol.Attributes;

namespace Vint.Core.ECS.Events.Entrance.Validation;

[ProtocolId(1437990639822)]
public class CheckUserUidEvent : IServerEvent {
    [ProtocolName("Uid")] public string Username { get; private set; } = null!;

    public async Task Execute(IPlayerConnection connection, IEntity[] entities) {
        await using DbConnection db = new();

        if (await db.Players.AnyAsync(player => player.Username == Username))
            await connection.Send(new UserUidOccupiedEvent(Username));
        else await connection.Send(new UserUidVacantEvent(Username));
    }
}
