using System.Net.Http.Json;
using DSharpPlus;
using DSharpPlus.Exceptions;
using DSharpPlus.Net;
using LinqToDB;
using LinqToDB.Mapping;
using Vint.Core.Discord;
using Vint.Core.Server.Game;

namespace Vint.Core.Database.Models;

[Table(DbConstants.DiscordLinks)]
public class DiscordLink {
    [PrimaryKey(0)] public required long PlayerId { get; init; }
    [PrimaryKey(1)] public required ulong UserId { get; init; }

    [Column] public required DateTimeOffset TokenExpirationDate { private get; set; }
    [Column(DataType = DataType.Text)] public required string AccessToken { get; set; }
    [Column(DataType = DataType.Text)] public required string RefreshToken { private get; set; }

    [NotColumn] static RestClientOptions RestClientOptions => new() { Timeout = DiscordBot.RequestTimeout };

    async Task<bool?> PrepareToken(DiscordBot discordBot) {
        if (DateTimeOffset.UtcNow >= TokenExpirationDate)
            await Refresh(discordBot);

        return await IsAuthorized(AccessToken);
    }

    async Task Refresh(DiscordBot discordBot) {
        Dictionary<string, string> data = new() {
            { "grant_type", "refresh_token" },
            { "client_id", discordBot.Id.ToString() },
            { "client_secret", discordBot.ClientSecret },
            { "refresh_token", RefreshToken }
        };

        using HttpClient httpClient = new();
        httpClient.Timeout = DiscordBot.RequestTimeout;
        HttpResponseMessage response = await httpClient.PostAsync("https://discord.com/api/v10/oauth2/token", new FormUrlEncodedContent(data));

        if (response.IsSuccessStatusCode) {
            OAuth2Data oAuth2Data = (await response.Content.ReadFromJsonAsync<OAuth2Data>())!;
            DateTimeOffset tokenExpirationDate = DateTimeOffset.UtcNow.AddSeconds(oAuth2Data.ExpiresIn - 300);

            await using DbConnection db = new();
            await db.DiscordLinks
                .Where(dLink => dLink.PlayerId == PlayerId && dLink.UserId == UserId)
                .Set(dLink => dLink.AccessToken, oAuth2Data.AccessToken)
                .Set(dLink => dLink.RefreshToken, oAuth2Data.RefreshToken)
                .Set(dLink => dLink.TokenExpirationDate, tokenExpirationDate)
                .UpdateAsync();

            AccessToken = oAuth2Data.AccessToken;
            RefreshToken = oAuth2Data.RefreshToken;
            TokenExpirationDate = tokenExpirationDate;
        }
    }

    static async Task<bool?> IsAuthorized(string accessToken) {
        try {
            await new DiscordRestClient(RestClientOptions, accessToken, TokenType.Bearer).InitializeAsync();
            return true;
        } catch (UnauthorizedException) {
            return false;
        } catch {
            return null;
        }
    }

    public async Task<(DiscordRestClient? client, bool? isAuthorized)> GetClient(IPlayerConnection connection, DiscordBot discordBot) {
        bool? isAuthorized = await PrepareToken(discordBot);

        switch (isAuthorized) {
            case null:
                return (null, null);

            case false: {
                await Revoke(discordBot, connection);
                return (null, false);
            }

            case true: {
                DiscordRestClient client = new(RestClientOptions, AccessToken, TokenType.Bearer);
                await client.InitializeAsync();
                return (client, true);
            }
        }
    }

    public async Task Revoke(DiscordBot discordBot, IPlayerConnection? connection) {
        await using DbConnection db = new();
        await db.BeginTransactionAsync();

        await db.Players
            .Where(player => player.Id == PlayerId && player.DiscordUserId == UserId)
            .Set(player => player.DiscordUserId, 0UL)
            .Set(player => player.DiscordLinked, false)
            .UpdateAsync();

        await db.DiscordLinks
            .Where(dLink => dLink.PlayerId == PlayerId && dLink.UserId == UserId)
            .DeleteAsync();

        await db.CommitTransactionAsync();

        if (connection != null && connection.Player.Id == PlayerId) {
            connection.Player.DiscordLinked = false;
            connection.Player.DiscordUserId = 0;
            connection.Player.DiscordLink = null!;
        }

        Dictionary<string, string> data = new() {
            { "token", RefreshToken },
            { "client_id", discordBot.Id.ToString() },
            { "client_secret", discordBot.ClientSecret }
        };

        using HttpClient httpClient = new();
        httpClient.Timeout = DiscordBot.RequestTimeout;
        await httpClient.PostAsync("https://discord.com/api/v10/oauth2/token/revoke", new FormUrlEncodedContent(data));

        await discordBot.RevokeLinkedRole(UserId);
    }
}
