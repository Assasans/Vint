using LinqToDB;
using Vint.Core.Database;
using Vint.Core.Database.Models;
using Vint.Core.ECS.Entities;
using Vint.Core.Server.Game;
using Vint.Core.Server.Game.Protocol.Attributes;

namespace Vint.Core.ECS.Events.User;

[ProtocolId(1451368548585)]
public class RequestLoadUserProfileEvent(
    GameServer server
) : IServerEvent {
    public long UserId { get; private set; }

    public async Task Execute(IPlayerConnection connection, IEntity[] entities) {
        if (!UserRegistry.TryGetContainer(UserId, out UserContainer? container)) {
            await using DbConnection db = new();

            Player? player = server.PlayerConnections.Values.Where(conn => conn.IsLoggedIn).SingleOrDefault(conn => conn.Player.Id == UserId)?.Player ??
                             await db.Players.SingleOrDefaultAsync(player => player.Id == UserId);

            if (player == null)
                throw new InvalidOperationException($"Player {UserId} not found");

            container = UserRegistry.GetOrCreateContainer(UserId, player);
        }

        await container.ShareTo(connection);
        await connection.Send(new UserProfileLoadedEvent(), container.Entity);
    }
}
