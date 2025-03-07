﻿using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using ConcurrentCollections;
using LinqToDB;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Vint.Core.Battle.Lobby;
using Vint.Core.Battle.Player;
using Vint.Core.Battle.Rounds;
using Vint.Core.Config;
using Vint.Core.Database;
using Vint.Core.Database.Models;
using Vint.Core.Discord;
using Vint.Core.ECS.Components.Battle.Rewards;
using Vint.Core.ECS.Components.Battle.User;
using Vint.Core.ECS.Components.Entrance;
using Vint.Core.ECS.Components.Group;
using Vint.Core.ECS.Components.Item;
using Vint.Core.ECS.Components.Modules;
using Vint.Core.ECS.Components.Notification;
using Vint.Core.ECS.Components.Preset;
using Vint.Core.ECS.Components.Server.Experience;
using Vint.Core.ECS.Components.Server.Login;
using Vint.Core.ECS.Components.User;
using Vint.Core.ECS.Entities;
using Vint.Core.ECS.Events;
using Vint.Core.ECS.Events.Action;
using Vint.Core.ECS.Events.Battle.Score;
using Vint.Core.ECS.Events.Entrance.Login;
using Vint.Core.ECS.Events.Items;
using Vint.Core.ECS.Events.Items.Module;
using Vint.Core.ECS.Events.Ping;
using Vint.Core.ECS.Templates;
using Vint.Core.ECS.Templates.Avatar;
using Vint.Core.ECS.Templates.Containers;
using Vint.Core.ECS.Templates.Covers;
using Vint.Core.ECS.Templates.Details;
using Vint.Core.ECS.Templates.Entrance;
using Vint.Core.ECS.Templates.Gold;
using Vint.Core.ECS.Templates.Graffiti;
using Vint.Core.ECS.Templates.Hulls;
using Vint.Core.ECS.Templates.Modules;
using Vint.Core.ECS.Templates.Money;
using Vint.Core.ECS.Templates.Notification;
using Vint.Core.ECS.Templates.Paints;
using Vint.Core.ECS.Templates.Premium;
using Vint.Core.ECS.Templates.Preset;
using Vint.Core.ECS.Templates.Shells;
using Vint.Core.ECS.Templates.Skins;
using Vint.Core.ECS.Templates.Weapons.Market;
using Vint.Core.ECS.Templates.Weapons.User;
using Vint.Core.Server.Game.Protocol.Codecs.Buffer;
using Vint.Core.Server.Game.Protocol.Codecs.Impl;
using Vint.Core.Server.Game.Protocol.Commands;
using Vint.Core.Utils;

namespace Vint.Core.Server.Game;

public interface IPlayerConnection : IAsyncDisposable, IDisposable {
    ILogger Logger { get; }

    Player Player { get; set; }
    LobbyPlayer? LobbyPlayer { get; set; }
    Spectator? Spectator { get; set; }
    UserContainer UserContainer { get; }
    IEntity ClientSession { get; }
    IServiceProvider ServiceProvider { get; }

    bool IsLoggedIn { get; }
    [MemberNotNullWhen(true, nameof(LobbyPlayer))]
    bool InLobby { get; }
    [MemberNotNullWhen(true, nameof(Spectator))]
    bool Spectating { get; }
    DateTimeOffset PingSendTime { set; }
    DateTimeOffset PongReceiveTime { set; }
    long Ping { get; }
    Invite? Invite { get; set; }

    string? RestorePasswordCode { get; set; }
    bool RestorePasswordCodeValid { get; set; }

    int BattleSeries { get; set; }

    ConcurrentHashSet<IEntity> SharedEntities { get; }

    Task Register(
        string username,
        string encryptedPasswordDigest,
        string email,
        string hardwareFingerprint,
        bool subscribed,
        bool steam,
        bool quickRegistration);

    Task Login(bool saveAutoLoginToken, bool rememberMe, string hardwareFingerprint);

    Task ChangePassword(string passwordDigest);

    Task ChangeReputation(int delta);

    Task ChangeExperience(int delta);

    Task ChangeGameplayChestScore(int delta);

    Task PurchaseItem(IEntity marketItem, int amount, int price, bool forXCrystals, bool mount);

    Task MountItem(IEntity userItem);

    Task AssembleModule(IEntity marketItem);

    Task UpgradeModule(IEntity userItem, bool forXCrystals);

    Task CheckLoginRewards();

    Task UpdateDeserterStatus(bool roundEnded, bool hasEnemies);

    Task<bool> OwnsItem(IEntity marketItem);

    Task<bool> CanOwnItem(IEntity marketItem);

    Task SetUsername(string username);

    Task ChangeCrystals(long delta);

    Task ChangeXCrystals(long delta);

    Task SetGoldBoxes(int goldBoxes);

    Task DisplayMessage(string message, TimeSpan? closeTime = null);

    Task SetClipboard(string content);

    Task OpenURL(string url);

    Task Kick(string? reason);

    Task Send(ICommand command);

    Task Send(IEvent @event);

    Task Send(IEvent @event, params IEnumerable<IEntity> entities);

    Task Share(IEntity entity);

    Task ShareIfUnshared(IEntity entity);

    Task Unshare(IEntity entity);

    Task UnshareIfShared(IEntity entity);

    void Schedule(TimeSpan delay, Func<Task> action);

    void Schedule(DateTimeOffset time, Func<Task> action);

    Task Tick();
}

public abstract class PlayerConnection(
    int id,
    IServiceProvider serviceProvider
) : IPlayerConnection {
    protected ConcurrentHashSet<DelayedTask> DelayedTasks { get; } = [];
    public int Id { get; } = id;
    public ILogger Logger { get; protected set; } = Log.Logger.ForType<PlayerConnection>();

    public Player Player { get; set; } = null!;
    public LobbyPlayer? LobbyPlayer { get; set; }
    public Spectator? Spectator { get; set; }
    public UserContainer UserContainer { get; private set; } = null!;
    public IEntity ClientSession { get; protected set; } = null!;
    public IServiceProvider ServiceProvider { get; } = serviceProvider;
    public int BattleSeries { get; set; }
    public ConcurrentHashSet<IEntity> SharedEntities { get; } = [];

    public string? RestorePasswordCode { get; set; }
    public bool RestorePasswordCodeValid { get; set; }

    public abstract bool IsLoggedIn { get; }
    public bool InLobby => LobbyPlayer != null;
    public bool Spectating => Spectator != null;
    public DateTimeOffset PingSendTime { get; set; }

    public DateTimeOffset PongReceiveTime {
        set => Ping = (long)(value - PingSendTime).TotalMilliseconds;
    }

    public long Ping { get; private set; }
    public Invite? Invite { get; set; }

    public async Task Register(
        string username,
        string encryptedPasswordDigest,
        string email,
        string hardwareFingerprint,
        bool subscribed,
        bool steam,
        bool quickRegistration) {
        Logger.Information("Registering player '{Username}'", username);

        byte[] passwordHash = new Encryption().RsaDecrypt(Convert.FromBase64String(encryptedPasswordDigest));

        Player = new Player {
            Id = EntityRegistry.GenerateId(),
            Username = username,
            Email = email,
            CountryCode = ClientSession.GetComponent<ClientLocaleComponent>().LocaleCode,
            HardwareFingerprint = hardwareFingerprint,
            Subscribed = subscribed,
            RegistrationTime = DateTimeOffset.UtcNow,
            LastLoginTime = DateTimeOffset.UtcNow,
            LastQuestUpdateTime = DateTimeOffset.UtcNow,
            LastLoginRewardTime = DateTimeOffset.Parse("2024-01-01 00:00:00"),
            PasswordHash = passwordHash
        };

        await using (DbConnection db = new()) {
            await db.InsertAsync(Player);

            if (Invite != null) {
                Invite.RemainingUses--;
                await db.UpdateAsync(Invite);
            }
        }

        await Player.InitializeNew();
        await Login(true, true, hardwareFingerprint);
    }

    public async Task Login(bool saveAutoLoginToken, bool rememberMe, string hardwareFingerprint) {
        Logger = Logger.WithPlayer((SocketPlayerConnection)this);

        Player.RememberMe = rememberMe;
        Player.LastLoginTime = DateTimeOffset.UtcNow;
        Player.HardwareFingerprint = hardwareFingerprint;

        if (saveAutoLoginToken) {
            Encryption encryption = new();

            byte[] autoLoginToken = new byte[32];
            new Random().NextBytes(autoLoginToken);

            byte[] encryptedAutoLoginToken = encryption.EncryptAutoLoginToken(autoLoginToken, Player.PasswordHash);

            Player.AutoLoginToken = autoLoginToken;
            await Send(new SaveAutoLoginTokenEvent(Player.Username, encryptedAutoLoginToken));
        }

        UserContainer = UserRegistry.GetOrCreateContainer(Player.Id, Player);
        await UserContainer.ShareTo(this);

        await ClientSession.AddGroupComponent<UserGroupComponent>(UserContainer.Entity);
        await UserContainer.Entity.AddComponent<UserOnlineComponent>();

        Logger.Warning("Logged in");

        await using DbConnection db = new();
        await db.UpdateAsync(Player);
    }

    public async Task ChangePassword(string passwordDigest) {
        byte[] passwordHash = new Encryption().RsaDecrypt(Convert.FromBase64String(passwordDigest));
        Player.PasswordHash = passwordHash;

        await using DbConnection db = new();

        await db
            .Players
            .Where(player => player.Id == Player.Id)
            .Set(player => player.PasswordHash, Player.PasswordHash)
            .UpdateAsync();
    }

    public async Task ChangeReputation(int delta) {
        DateOnly date = DateOnly.FromDateTime(DateTime.Today);

        await using DbConnection db = new();
        await db.BeginTransactionAsync();

        SeasonStatistics seasonStats = await db.SeasonStatistics.SingleAsync(stats =>
            stats.PlayerId == Player.Id && stats.SeasonNumber == ConfigManager.ServerConfig.SeasonNumber);

        ReputationStatistics? reputationStats =
            await db.ReputationStatistics.SingleOrDefaultAsync(repStats => repStats.PlayerId == Player.Id && repStats.Date == date);

        League oldLeagueIndex = Player.League;
        uint oldReputation = Player.Reputation;

        reputationStats ??= new ReputationStatistics {
            Player = Player,
            Date = date,
            SeasonNumber = ConfigManager.ServerConfig.SeasonNumber
        };

        uint reputation = (uint)Math.Clamp(oldReputation + delta, 0, 99999);

        Player.Reputation = reputation;
        seasonStats.Reputation = reputation;
        reputationStats.Reputation = reputation;

        await UserContainer.Entity.ChangeComponent<UserReputationComponent>(component => component.Reputation = reputation);

        if (oldLeagueIndex != Player.League) {
            await UserContainer.Entity.RemoveComponent<LeagueGroupComponent>();
            await UserContainer.Entity.AddGroupComponent<LeagueGroupComponent>(Player.LeagueEntity);
        }

        if (seasonStats.Reputation != oldReputation)
            await db.UpdateAsync(seasonStats);

        await db.InsertOrReplaceAsync(reputationStats);

        if ((Player.RewardedLeagues & Player.League) != Player.League) {
            Dictionary<IEntity, int> rewards = Leveling.GetFirstLeagueEntranceReward(Player.League);

            if (rewards.Count != 0) {
                foreach ((IEntity entity, int amount) in rewards)
                    await PurchaseItem(entity, amount, 0, false, false);

                IEntity rewardNotification = new LeagueFirstEntranceRewardPersistentNotificationTemplate().Create(rewards);
                await Share(rewardNotification);
            }

            Player.RewardedLeagues |= Player.League;
        }

        await db.UpdateAsync(Player);
        await db.CommitTransactionAsync();
    }

    public async Task ChangeExperience(int delta) {
        await using DbConnection db = new();
        await db.BeginTransactionAsync();

        await db
            .Players
            .Where(player => player.Id == Player.Id)
            .Set(player => player.Experience, player => player.Experience + delta)
            .UpdateAsync();

        await db
            .SeasonStatistics
            .Where(stats => stats.PlayerId == Player.Id && stats.SeasonNumber == ConfigManager.ServerConfig.SeasonNumber)
            .Set(stats => stats.ExperienceEarned, stats => stats.ExperienceEarned + delta)
            .UpdateAsync();

        await db.CommitTransactionAsync();
        Player.Experience += delta;
        await UserContainer.Entity.ChangeComponent<UserExperienceComponent>(component => component.Experience = Player.Experience);

        await CheckRankUp();
    }

    public async Task ChangeGameplayChestScore(int delta) {
        const int scoreLimit = 1000;

        Player.GameplayChestScore += delta;
        int earned = (int)Math.Floor((double)Player.GameplayChestScore / scoreLimit);

        if (earned != 0) {
            Player.GameplayChestScore -= earned * scoreLimit;

            await PurchaseItem(Player.LeagueEntity.GetComponent<ChestBattleRewardComponent>()
                    .Chest,
                earned,
                0,
                false,
                false);
        }

        try {
            await using DbConnection db = new();
            await db.UpdateAsync(Player);
        } catch (Exception e) {
            Logger.Error(e, "Failed to update gameplay chest score in database");
            return;
        }

        await UserContainer.Entity.ChangeComponent<GameplayChestScoreComponent>(component => component.Current = Player.GameplayChestScore);
    }

    async Task CheckRankUp() {
        UserRankComponent rankComponent = UserContainer.Entity.GetComponent<UserRankComponent>();

        while (rankComponent.Rank < Player.Rank) {
            rankComponent.Rank++;
            await UserContainer.Entity.ChangeComponent(rankComponent);

            int rankIndex = rankComponent.Rank - 1;
            int crystals = CalculateCrystals(rankIndex);
            int xCrystals = CalculateXCrystals(rankIndex);

            CreateByRankConfigComponent createByRankConfigComponent = ConfigManager.GetComponent<CreateByRankConfigComponent>("garage/preset");

            if (createByRankConfigComponent.UserRankListToCreateItem.Contains(rankComponent.Rank))
                await PurchaseItem(GlobalEntities.GetEntity("misc", "Preset"), 1, 0, false, false);

            await ChangeCrystals(crystals);
            await ChangeXCrystals(xCrystals);
            await Share(new UserRankRewardNotificationTemplate().Create(rankComponent.Rank, crystals, xCrystals));

            if (InLobby && LobbyPlayer!.InRound)
                await LobbyPlayer.Round.Players.Send(new UpdateRankEvent(), UserContainer.Entity);
        }

        return;

        static int CalculateCrystals(int rankIndex) => rankIndex switch {
            < 9 => 100,
            < 12 => 500,
            < 14 => 1200,
            < 19 => 3000,
            < 49 => 3500,
            < 99 => 4000,
            _ => 7500
        };

        static int CalculateXCrystals(int rankIndex) =>
            rankIndex == 100 ? 100
                : rankIndex % 10 == 0 ? 50
                    : rankIndex % 5 == 0 ? 20
                        : 0;
    }

    public async Task PurchaseItem(IEntity marketItem, int amount, int price, bool forXCrystals, bool mount) {
        await using DbConnection db = new();
        IEntity? userItem = null;
        EntityTemplate? template = marketItem.TemplateAccessor?.Template;

        switch (template) {
            case AvatarMarketItemTemplate: {
                await db.InsertAsync(new Avatar { Player = Player, Id = marketItem.Id });
                break;
            }

            case GraffitiMarketItemTemplate:
            case ChildGraffitiMarketItemTemplate: {
                await db.InsertAsync(new Graffiti { Player = Player, Id = marketItem.Id });
                break;
            }

            case CrystalMarketItemTemplate: {
                await ChangeCrystals(amount);
                mount = false;
                break;
            }

            case XCrystalMarketItemTemplate: {
                await ChangeXCrystals(amount);
                mount = false;
                break;
            }

            case GoldBonusMarketItemTemplate: {
                await SetGoldBoxes(Player.GoldBoxItems + amount);
                mount = false;
                break;
            }

            case TankMarketItemTemplate: {
                long skinId = GlobalEntities.DefaultSkins[marketItem.Id];
                IEntity skin = GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == skinId);

                await db.InsertAsync(new Hull { Player = Player, Id = marketItem.Id, SkinId = skinId });
                await PurchaseItem(skin, 1, 0, false, false);
                await MountItem(skin.GetUserEntity(this));
                break;
            }

            case WeaponMarketItemTemplate: {
                long skinId = GlobalEntities.DefaultSkins[marketItem.Id];
                long shellId = GlobalEntities.DefaultShells[marketItem.Id];

                IEntity skin = GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == skinId);
                IEntity shell = GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == shellId);

                await db.InsertAsync(new Weapon { Player = Player, Id = marketItem.Id, SkinId = skinId, ShellId = shellId });
                await PurchaseItem(skin, 1, 0, false, false);
                await PurchaseItem(shell, 1, 0, false, false);

                await MountItem(skin.GetUserEntity(this));
                await MountItem(shell.GetUserEntity(this));
                break;
            }

            case HullSkinMarketItemTemplate: {
                long hullId = marketItem.GetComponent<ParentGroupComponent>().Key;

                if (!await db.Hulls.AnyAsync(hull => hull.PlayerId == Player.Id && hull.Id == hullId)) return;

                await db.InsertAsync(new HullSkin { Player = Player, Id = marketItem.Id, HullId = hullId });
                break;
            }

            case WeaponSkinMarketItemTemplate: {
                long weaponId = marketItem.GetComponent<ParentGroupComponent>().Key;

                if (!await db.Weapons.AnyAsync(weapon => weapon.PlayerId == Player.Id && weapon.Id == weaponId)) return;

                await db.InsertAsync(new WeaponSkin { Player = Player, Id = marketItem.Id, WeaponId = weaponId });
                break;
            }

            case TankPaintMarketItemTemplate: {
                await db.InsertAsync(new Paint { Player = Player, Id = marketItem.Id });
                break;
            }

            case WeaponPaintMarketItemTemplate: {
                await db.InsertAsync(new Cover { Player = Player, Id = marketItem.Id });
                break;
            }

            case ShellMarketItemTemplate: {
                long weaponId = marketItem.GetComponent<ParentGroupComponent>().Key;

                if (!await db.Weapons.AnyAsync(weapon => weapon.PlayerId == Player.Id && weapon.Id == weaponId)) return;

                await db.InsertAsync(new Shell { Player = Player, Id = marketItem.Id, WeaponId = weaponId });
                break;
            }

            case ModuleCardMarketItemTemplate: {
                long moduleId = marketItem.GetComponent<ParentGroupComponent>().Key;

                Module? module = Player.Modules.SingleOrDefault(module => module.Id == moduleId);

                if (module == null) {
                    module = new Module { Player = Player, Id = moduleId };
                    Player.Modules.Add(module);
                }

                module.Cards += amount;
                await db.InsertOrReplaceAsync(module);
                break;
            }

            case DonutChestMarketItemTemplate:
            case GameplayChestMarketItemTemplate:
            case ContainerPackPriceMarketItemTemplate:
            case TutorialGameplayChestMarketItemTemplate: {
                Container? container = await db.Containers.SingleOrDefaultAsync(cont => cont.PlayerId == Player.Id && cont.Id == marketItem.Id);

                if (container == null) {
                    container = new Container { Player = Player, Id = marketItem.Id, Count = amount };
                    await db.InsertAsync(container);
                } else {
                    container.Count += amount;
                    await db.UpdateAsync(container);
                }

                mount = false;
                break;
            }

            case PremiumBoostMarketItemTemplate:
            case PremiumQuestMarketItemTemplate:
                Logger.Information("User purchased Premium Boost or Quest. Action is not implemented");
                break;

            case PresetMarketItemTemplate: {
                List<Preset> presets = await db
                    .Presets
                    .Where(preset => preset.PlayerId == Player.Id)
                    .ToListAsync();

                Preset preset = new() { Player = Player, Index = presets.Count, Name = $"Preset {presets.Count + 1}" };
                userItem = GlobalEntities.GetEntity("misc", "Preset");

                userItem.TemplateAccessor!.Template = ((MarketEntityTemplate)userItem.TemplateAccessor.Template).UserTemplate;
                userItem.Id = EntityRegistry.GenerateId();

                await userItem.AddComponent(new PresetEquipmentComponent(preset));
                await userItem.AddComponent(new PresetNameComponent { Name = preset.Name });

                preset.Entity = userItem;
                Player.UserPresets.Add(preset);

                await db.InsertAsync(preset);
                await Share(userItem);
                break;
            }

            case DetailMarketItemTemplate: {
                Detail? detail = await db.Details.SingleOrDefaultAsync(detail => detail.PlayerId == Player.Id && detail.Id == marketItem.Id);

                if (detail == null) {
                    detail = new Detail { PlayerId = Player.Id, Id = marketItem.Id, Count = amount };
                    await db.InsertAsync(detail);
                } else {
                    detail.Count += amount;
                    await db.UpdateAsync(detail);
                }

                mount = false;
                break;
            }

            default:
                Logger.Error("{Name} purchase is not implemented", template?.GetType().FullName);
                throw new NotImplementedException();
        }

        userItem ??= marketItem.GetUserEntity(this);
        await userItem.AddComponentIfAbsent(new UserGroupComponent(UserContainer.Entity));

        if (price > 0) {
            if (forXCrystals) await ChangeXCrystals(-price);
            else await ChangeCrystals(-price);
        }

        if (userItem.HasComponent<UserItemCounterComponent>()) {
            await userItem.ChangeComponent<UserItemCounterComponent>(component => component.Count += amount);
            await Send(new ItemsCountChangedEvent(amount), userItem);
        }

        if (mount) await MountItem(userItem);
    }

    public async Task MountItem(IEntity userItem) {
        bool changeEquipment = false;
        Preset currentPreset = Player.CurrentPreset;
        IEntity marketItem = userItem.GetMarketEntity(this);

        await using (DbConnection db = new()) {
            switch (userItem.TemplateAccessor!.Template) {
                case AvatarUserItemTemplate: {
                    await this.GetEntity(Player.CurrentAvatarId)!
                        .GetUserEntity(this)
                        .RemoveComponent<MountedItemComponent>();

                    await userItem.AddComponent<MountedItemComponent>();

                    Player.CurrentAvatarId = marketItem.Id;
                    await UserContainer.Entity.ChangeComponent(new UserAvatarComponent(Player.CurrentAvatarId));

                    await db.UpdateAsync(Player);
                    break;
                }

                case GraffitiUserItemTemplate: {
                    await currentPreset
                        .Graffiti
                        .GetUserEntity(this)
                        .RemoveComponent<MountedItemComponent>();

                    currentPreset.Graffiti = marketItem;
                    await userItem.AddComponent<MountedItemComponent>();

                    await db.UpdateAsync(currentPreset);
                    break;
                }

                case TankUserItemTemplate: {
                    changeEquipment = true;

                    await currentPreset
                        .Hull
                        .GetUserEntity(this)
                        .RemoveComponent<MountedItemComponent>();

                    currentPreset.Hull = marketItem;
                    await userItem.AddComponent<MountedItemComponent>();

                    currentPreset.Entity!
                        .GetComponent<PresetEquipmentComponent>()
                        .SetHullId(currentPreset.Hull.Id);

                    Hull newHull = await db
                        .Hulls
                        .Where(hull => hull.PlayerId == Player.Id)
                        .SingleAsync(hull => hull.Id == currentPreset.Hull.Id);

                    IEntity skin = GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == newHull.SkinId);

                    await currentPreset
                        .HullSkin
                        .GetUserEntity(this)
                        .RemoveComponent<MountedItemComponent>();

                    currentPreset.HullSkin = skin;

                    await currentPreset
                        .HullSkin
                        .GetUserEntity(this)
                        .AddComponentIfAbsent<MountedItemComponent>();

                    await db.UpdateAsync(currentPreset);
                    break;
                }

                case WeaponUserItemTemplate: {
                    changeEquipment = true;

                    await currentPreset
                        .Weapon
                        .GetUserEntity(this)
                        .RemoveComponent<MountedItemComponent>();

                    currentPreset.Weapon = marketItem;
                    await userItem.AddComponent<MountedItemComponent>();

                    currentPreset.Entity!
                        .GetComponent<PresetEquipmentComponent>()
                        .SetWeaponId(currentPreset.Weapon.Id);

                    Weapon newWeapon = await db
                        .Weapons
                        .Where(weapon => weapon.PlayerId == Player.Id)
                        .SingleAsync(weapon => weapon.Id == currentPreset.Weapon.Id);

                    IEntity skin = GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == newWeapon.SkinId);
                    IEntity shell = GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == newWeapon.ShellId);

                    await currentPreset
                        .WeaponSkin
                        .GetUserEntity(this)
                        .RemoveComponent<MountedItemComponent>();

                    currentPreset.WeaponSkin = skin;

                    await currentPreset
                        .WeaponSkin
                        .GetUserEntity(this)
                        .AddComponentIfAbsent<MountedItemComponent>();

                    await currentPreset
                        .Shell
                        .GetUserEntity(this)
                        .RemoveComponent<MountedItemComponent>();

                    currentPreset.Shell = shell;

                    await currentPreset
                        .Shell
                        .GetUserEntity(this)
                        .AddComponentIfAbsent<MountedItemComponent>();

                    await db.UpdateAsync(currentPreset);
                    break;
                }

                case HullSkinUserItemTemplate: {
                    HullSkin skin = await db
                        .HullSkins
                        .Where(skin => skin.PlayerId == Player.Id)
                        .SingleAsync(skin => skin.Id == marketItem.Id);

                    bool isCurrentHull = skin.HullId == currentPreset.Hull.Id;

                    if (!isCurrentHull) {
                        Hull? newHull = await db.Hulls.SingleOrDefaultAsync(hull => hull.PlayerId == Player.Id && hull.Id == skin.HullId);

                        if (newHull == null) return;

                        IEntity newUserHull = this.GetEntity(newHull.Id)!.GetUserEntity(this);
                        await MountItem(newUserHull);
                    }

                    await currentPreset
                        .HullSkin
                        .GetUserEntity(this)
                        .RemoveComponentIfPresent<MountedItemComponent>();

                    currentPreset.HullSkin = marketItem;
                    await userItem.AddComponent<MountedItemComponent>();

                    await db
                        .Hulls
                        .Where(hull => hull.PlayerId == Player.Id && hull.Id == skin.HullId)
                        .Set(hull => hull.SkinId, skin.Id)
                        .UpdateAsync();

                    await db.UpdateAsync(currentPreset);
                    break;
                }

                case WeaponSkinUserItemTemplate: {
                    WeaponSkin skin = await db
                        .WeaponSkins
                        .Where(skin => skin.PlayerId == Player.Id)
                        .SingleAsync(skin => skin.Id == marketItem.Id);

                    bool isCurrentWeapon = skin.WeaponId == currentPreset.Weapon.Id;

                    if (!isCurrentWeapon) {
                        Weapon? newWeapon =
                            await db.Weapons.SingleOrDefaultAsync(weapon => weapon.PlayerId == Player.Id && weapon.Id == skin.WeaponId);

                        if (newWeapon == null) return;

                        IEntity newUserWeapon = this.GetEntity(newWeapon.Id)!.GetUserEntity(this);
                        await MountItem(newUserWeapon);
                    }

                    await currentPreset
                        .WeaponSkin
                        .GetUserEntity(this)
                        .RemoveComponentIfPresent<MountedItemComponent>();

                    currentPreset.WeaponSkin = marketItem;
                    await userItem.AddComponent<MountedItemComponent>();

                    await db
                        .Weapons
                        .Where(weapon => weapon.PlayerId == Player.Id && weapon.Id == currentPreset.Weapon.Id)
                        .Set(weapon => weapon.SkinId, currentPreset.WeaponSkin.Id)
                        .UpdateAsync();

                    await db.UpdateAsync(currentPreset);
                    break;
                }

                case TankPaintUserItemTemplate: {
                    await currentPreset
                        .Paint
                        .GetUserEntity(this)
                        .RemoveComponent<MountedItemComponent>();

                    currentPreset.Paint = marketItem;
                    await userItem.AddComponent<MountedItemComponent>();

                    await db.UpdateAsync(currentPreset);
                    break;
                }

                case WeaponPaintUserItemTemplate: {
                    await currentPreset
                        .Cover
                        .GetUserEntity(this)
                        .RemoveComponent<MountedItemComponent>();

                    currentPreset.Cover = marketItem;
                    await userItem.AddComponent<MountedItemComponent>();

                    await db.UpdateAsync(currentPreset);
                    break;
                }

                case ShellUserItemTemplate: {
                    Shell shell = await db
                        .Shells
                        .Where(shell => shell.PlayerId == Player.Id)
                        .SingleAsync(shell => shell.Id == marketItem.Id);

                    bool isCurrentWeapon = shell.WeaponId == currentPreset.Weapon.Id;

                    if (!isCurrentWeapon) {
                        Weapon? newWeapon =
                            await db.Weapons.SingleOrDefaultAsync(weapon => weapon.PlayerId == Player.Id && weapon.Id == shell.WeaponId);

                        if (newWeapon == null) return;

                        IEntity newUserWeapon = this.GetEntity(newWeapon.Id)!.GetUserEntity(this);
                        await MountItem(newUserWeapon);
                    }

                    await currentPreset
                        .Shell
                        .GetUserEntity(this)
                        .RemoveComponentIfPresent<MountedItemComponent>();

                    currentPreset.Shell = marketItem;
                    await userItem.AddComponent<MountedItemComponent>();

                    await db
                        .Weapons
                        .Where(weapon => weapon.PlayerId == Player.Id && weapon.Id == currentPreset.Weapon.Id)
                        .Set(weapon => weapon.ShellId, currentPreset.Shell.Id)
                        .UpdateAsync();

                    await db.UpdateAsync(currentPreset);
                    break;
                }

                case PresetUserItemTemplate: {
                    changeEquipment = true;
                    Preset? newPreset = Player.UserPresets.SingleOrDefault(preset => preset.Entity == userItem);

                    if (newPreset == null) return;

                    Dictionary<IEntity, IEntity> slotToCurrentModule = currentPreset.Modules.ToDictionary(pModule => pModule.GetSlotEntity(this),
                        pModule => pModule.Entity.GetUserModule(this));

                    Dictionary<IEntity, IEntity> slotToNewModule = newPreset.Modules.ToDictionary(pModule => pModule.GetSlotEntity(this),
                        pModule => pModule.Entity.GetUserModule(this));

                    foreach (IEntity entity in new[] {
                                 currentPreset.Hull.GetUserEntity(this),
                                 currentPreset.Paint.GetUserEntity(this),
                                 currentPreset.HullSkin.GetUserEntity(this),
                                 currentPreset.Weapon.GetUserEntity(this),
                                 currentPreset.Cover.GetUserEntity(this),
                                 currentPreset.WeaponSkin.GetUserEntity(this),
                                 currentPreset.Shell.GetUserEntity(this),
                                 currentPreset.Graffiti.GetUserEntity(this),
                                 currentPreset.Entity!
                             }) {
                        await entity.RemoveComponentIfPresent<MountedItemComponent>();
                    }

                    foreach (IEntity entity in new[] {
                                 newPreset.Hull.GetUserEntity(this),
                                 newPreset.Paint.GetUserEntity(this),
                                 newPreset.HullSkin.GetUserEntity(this),
                                 newPreset.Weapon.GetUserEntity(this),
                                 newPreset.Cover.GetUserEntity(this),
                                 newPreset.WeaponSkin.GetUserEntity(this),
                                 newPreset.Shell.GetUserEntity(this),
                                 newPreset.Graffiti.GetUserEntity(this),
                                 newPreset.Entity!
                             }) {
                        await entity.AddComponentIfAbsent<MountedItemComponent>();
                    }

                    foreach ((IEntity slot, IEntity module) in slotToCurrentModule) {
                        await slot.RemoveComponent<ModuleGroupComponent>();
                        await module.RemoveComponent<MountedItemComponent>();
                    }

                    foreach ((IEntity slot, IEntity module) in slotToNewModule) {
                        await slot.AddComponentFrom<ModuleGroupComponent>(module);
                        await module.AddComponent<MountedItemComponent>();
                    }

                    Player.CurrentPresetIndex = newPreset.Index;

                    await db
                        .Players
                        .Where(player => player.Id == Player.Id)
                        .Set(player => player.CurrentPresetIndex, () => Player.CurrentPresetIndex)
                        .UpdateAsync();

                    break;
                }

                default: throw new NotImplementedException();
            }
        }

        if (!changeEquipment ||
            !UserContainer.Entity.HasComponent<UserEquipmentComponent>()) return;

        await UserContainer.Entity.RemoveComponent<UserEquipmentComponent>();
        await UserContainer.Entity.AddComponent(new UserEquipmentComponent(Player.CurrentPreset.Weapon.Id, Player.CurrentPreset.Hull.Id));
    }

    public async Task AssembleModule(IEntity marketItem) {
        await using DbConnection db = new();
        Module? module = Player.Modules.SingleOrDefault(module => module.Id == marketItem.Id);

        if (module is not { Level: -1, Cards: > 0 }) {
            Logger.Error("Module {Id} is not ready to assemble", marketItem.Id);
            return;
        }

        module.Cards -= marketItem.GetComponent<ModuleCardsCompositionComponent>()
            .CraftPrice.Cards;

        module.Level++;

        await db.UpdateAsync(module);

        IEntity card = SharedEntities.Single(entity => entity.TemplateAccessor?.Template is ModuleCardUserItemTemplate &&
                                                       entity.GetComponent<ParentGroupComponent>()
                                                           .Key ==
                                                       marketItem.Id);

        IEntity userItem = marketItem.GetUserModule(this);

        await card.ChangeComponent<UserItemCounterComponent>(component => component.Count = module.Cards);
        await userItem.ChangeComponent<ModuleUpgradeLevelComponent>(component => component.Level = module.Level);
        await userItem.AddGroupComponent<UserGroupComponent>(UserContainer.Entity);

        await Send(new ModuleAssembledEvent(), userItem);
    }

    public async Task UpgradeModule(IEntity userItem, bool forXCrystals) {
        long id = userItem.GetComponent<ParentGroupComponent>().Key;

        Module? module = Player.Modules.SingleOrDefault(module => module.Id == id);
        ModuleCardsCompositionComponent compositionComponent = userItem.GetComponent<ModuleCardsCompositionComponent>();

        if (module == null ||
            module.Level >= compositionComponent.UpgradePrices.Count) {
            Logger.Error("Module {Id} is not upgradable", id);
            return;
        }

        ModulePrice price = compositionComponent.UpgradePrices[module.Level];

        if (module.Cards < price.Cards) {
            Logger.Error("Not enough cards to upgrade module {Id}", id);
            return;
        }

        bool crystalsEnough = forXCrystals
            ? price.XCrystals <= Player.XCrystals
            : price.Crystals <= Player.Crystals;

        if (!crystalsEnough) {
            Logger.Error("Not enough (x)crystals to upgrade module {Id}", id);
            return;
        }

        if (forXCrystals) await ChangeXCrystals(-price.XCrystals);
        else await ChangeCrystals(-price.Crystals);

        module.Cards -= price.Cards;
        module.Level++;

        await using DbConnection db = new();
        await db.UpdateAsync(module);

        IEntity card = SharedEntities.Single(entity => entity.TemplateAccessor?.Template is ModuleCardUserItemTemplate &&
                                                       entity.GetComponent<ParentGroupComponent>().Key == id);

        await card.ChangeComponent<UserItemCounterComponent>(component => component.Count = module.Cards);
        await userItem.ChangeComponent<ModuleUpgradeLevelComponent>(component => component.Level = module.Level);

        await Send(new ModuleUpgradedEvent(), userItem);
    }

public async Task UpdateDeserterStatus(bool roundEnded, bool hasEnemies) {
        IEntity user = UserContainer.Entity;

        BattleLeaveCounterComponent battleLeaveCounter = user.GetComponent<BattleLeaveCounterComponent>();
        long lefts = battleLeaveCounter.Value;
        int needGoodBattles = battleLeaveCounter.NeedGoodBattles;

        if (roundEnded) { // player played the battle to the end
            if (needGoodBattles > 0)
                needGoodBattles--;

            if (needGoodBattles == 0)
                lefts = 0;
        } else if (hasEnemies) { // player left the battle before it ended and there were enemies
            BattleSeries = 0;
            lefts++;

            if (lefts >= 2)
                needGoodBattles = needGoodBattles > 0
                    ? (int)lefts / 2
                    : 2;
        }

        battleLeaveCounter.Value = lefts;
        battleLeaveCounter.NeedGoodBattles = needGoodBattles;

        Player.DesertedBattlesCount = battleLeaveCounter.Value;
        Player.NeedGoodBattlesCount = battleLeaveCounter.NeedGoodBattles;

        await using DbConnection db = new();
        await db.Players
            .Where(p => p.Id == Player.Id)
            .Set(p => p.DesertedBattlesCount, Player.DesertedBattlesCount)
            .Set(p => p.NeedGoodBattlesCount, Player.NeedGoodBattlesCount)
            .UpdateAsync();

        await user.ChangeComponent(battleLeaveCounter);
    }

    public async Task CheckLoginRewards() {
        LoginRewardsComponent loginRewardsComponent = ConfigManager.GetComponent<LoginRewardsComponent>("login_rewards");
        long battles = UserContainer.Entity.GetComponent<UserStatisticsComponent>().Statistics["BATTLES_PARTICIPATED"];

        if (Player.NextLoginRewardTime.Value > DateTimeOffset.UtcNow ||
            Player.LastLoginRewardDay >= loginRewardsComponent.MaxDay ||
            battles < loginRewardsComponent.BattleCountToUnlock) return;

        int day = Player.LastLoginRewardDay + 1;

        List<LoginRewardItem> loginRewards = loginRewardsComponent
            .GetRewardsByDay(day)
            .ToList();

        foreach (LoginRewardItem reward in loginRewards) {
            IEntity? entity = this.GetEntity(reward.MarketItemEntity);

            if (entity == null || await OwnsItem(entity)) continue;

            await PurchaseItem(entity, reward.Amount, 0, false, false);
        }

        Player.LastLoginRewardDay = day;
        Player.LastLoginRewardTime = DateTimeOffset.UtcNow;
        Player.ResetNextLoginRewardTime();

        await using DbConnection db = new();
        await db.Players
            .Where(p => p.Id == Player.Id)
            .Set(p => p.LastLoginRewardDay, Player.LastLoginRewardDay)
            .Set(p => p.LastLoginRewardTime, Player.LastLoginRewardTime)
            .UpdateAsync();

        IEntity notification = new LoginRewardNotificationTemplate().Create(loginRewards, loginRewardsComponent.Rewards, day);
        await Share(notification);
    }

    public async Task<bool> OwnsItem(IEntity marketItem) {
        await using DbConnection db = new();

        return marketItem.TemplateAccessor!.Template switch {
            AvatarMarketItemTemplate => await db.Avatars.AnyAsync(avatar => avatar.PlayerId == Player.Id && avatar.Id == marketItem.Id),
            TankMarketItemTemplate => await db.Hulls.AnyAsync(hull => hull.PlayerId == Player.Id && hull.Id == marketItem.Id),
            WeaponMarketItemTemplate => await db.Weapons.AnyAsync(weapon => weapon.PlayerId == Player.Id && weapon.Id == marketItem.Id),
            HullSkinMarketItemTemplate => await db.HullSkins.AnyAsync(hullSkin => hullSkin.PlayerId == Player.Id && hullSkin.Id == marketItem.Id),
            WeaponSkinMarketItemTemplate => await db.WeaponSkins.AnyAsync(weaponSkin =>
                weaponSkin.PlayerId == Player.Id && weaponSkin.Id == marketItem.Id),
            TankPaintMarketItemTemplate => await db.Paints.AnyAsync(paint => paint.PlayerId == Player.Id && paint.Id == marketItem.Id),
            WeaponPaintMarketItemTemplate => await db.Covers.AnyAsync(cover => cover.PlayerId == Player.Id && cover.Id == marketItem.Id),
            ShellMarketItemTemplate => await db.Shells.AnyAsync(shell => shell.PlayerId == Player.Id && shell.Id == marketItem.Id),
            GraffitiMarketItemTemplate => await db.Graffities.AnyAsync(graffiti => graffiti.PlayerId == Player.Id && graffiti.Id == marketItem.Id),
            ChildGraffitiMarketItemTemplate => await db.Graffities.AnyAsync(
                graffiti => graffiti.PlayerId == Player.Id && graffiti.Id == marketItem.Id),
            // ContainerPackPriceMarketItemTemplate => await db.Containers.AnyAsync(container =>
            //     container.PlayerId == Player.Id && container.Id == marketItem.Id),
            // DonutChestMarketItemTemplate => await db.Containers.AnyAsync(chest => chest.PlayerId == Player.Id && chest.Id == marketItem.Id),
            // GameplayChestMarketItemTemplate => await db.Containers.AnyAsync(chest => chest.PlayerId == Player.Id && chest.Id == marketItem.Id),
            // TutorialGameplayChestMarketItemTemplate =>
            //     await db.Containers.AnyAsync(chest => chest.PlayerId == Player.Id && chest.Id == marketItem.Id),
            _ => false
        };
    }

    public async Task<bool> CanOwnItem(IEntity marketItem) {
        bool alreadyOwned = await OwnsItem(marketItem);

        if (alreadyOwned)
            return false;

        if (!marketItem.HasComponent<ParentGroupComponent>())
            return true;

        EntityTemplate? template = marketItem.TemplateAccessor?.Template;

        if (template == null)
            return false;

        if (template is not (HullSkinMarketItemTemplate or WeaponSkinMarketItemTemplate or ShellMarketItemTemplate))
            return true;

        long parentId = marketItem.GetComponent<ParentGroupComponent>().Key;
        IEntity parent = GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == parentId);

        return await OwnsItem(parent);
    }

    public virtual async Task SetUsername(string username) {
        Logger.Warning("Changed username => '{New}'", username);
        Player.Username = username;
        await UserContainer.Entity.ChangeComponent<UserUidComponent>(component => component.Username = username);

        await using DbConnection db = new();

        await db
            .Players
            .Where(player => player.Id == Player.Id)
            .Set(player => player.Username, username)
            .UpdateAsync();
    }

    public async Task ChangeCrystals(long delta) {
        if (delta == 0) return;

        await using DbConnection db = new();
        await db.BeginTransactionAsync();

        if (delta > 0) {
            await db
                .Statistics
                .Where(stats => stats.PlayerId == Player.Id)
                .Set(stats => stats.CrystalsEarned, stats => stats.CrystalsEarned + (ulong)delta)
                .UpdateAsync();

            await db
                .SeasonStatistics
                .Where(stats => stats.PlayerId == Player.Id && stats.SeasonNumber == ConfigManager.ServerConfig.SeasonNumber)
                .Set(stats => stats.CrystalsEarned, stats => stats.CrystalsEarned + (ulong)delta)
                .UpdateAsync();
        }

        await db
            .Players
            .Where(player => player.Id == Player.Id)
            .Set(player => player.Crystals, player => player.Crystals + delta)
            .UpdateAsync();

        await db.CommitTransactionAsync();
        Player.Crystals += delta;
        await UserContainer.Entity.ChangeComponent<UserMoneyComponent>(component => component.Money = Player.Crystals);
    }

    public async Task ChangeXCrystals(long delta) {
        await using DbConnection db = new();
        await db.BeginTransactionAsync();

        if (delta > 0) {
            await db
                .Statistics
                .Where(stats => stats.PlayerId == Player.Id)
                .Set(stats => stats.XCrystalsEarned, stats => stats.XCrystalsEarned + (ulong)delta)
                .UpdateAsync();

            await db
                .SeasonStatistics
                .Where(stats => stats.PlayerId == Player.Id && stats.SeasonNumber == ConfigManager.ServerConfig.SeasonNumber)
                .Set(stats => stats.XCrystalsEarned, stats => stats.XCrystalsEarned + (ulong)delta)
                .UpdateAsync();
        }

        await db
            .Players
            .Where(player => player.Id == Player.Id)
            .Set(player => player.XCrystals, player => player.XCrystals + delta)
            .UpdateAsync();

        await db.CommitTransactionAsync();
        Player.XCrystals += delta;
        await UserContainer.Entity.ChangeComponent<UserXCrystalsComponent>(component => component.Money = Player.XCrystals);
    }

    public async Task SetGoldBoxes(int goldBoxes) {
        await using DbConnection db = new();

        await db
            .Players
            .Where(player => player.Id == Player.Id)
            .Set(player => player.GoldBoxItems, goldBoxes)
            .UpdateAsync();

        Player.GoldBoxItems = goldBoxes;
    }

    public async Task DisplayMessage(string message, TimeSpan? closeTime = null) {
        IEntity notification = new SimpleTextNotificationTemplate().Create(message);

        await Share(notification);
        Schedule(closeTime ?? TimeSpan.FromSeconds(15), async () => await UnshareIfShared(notification));
    }

    public async Task SetClipboard(string content) {
        await Send(new SetClipboardEvent(content));
        await Share(new ClipboardSetNotificationTemplate().Create(UserContainer.Entity));
    }

    public Task OpenURL(string url) => Send(new OpenURLEvent(url));

    public abstract Task Kick(string? reason);

    public abstract Task Send(ICommand command);

    public Task Send(IEvent @event) => Send(@event, ClientSession);

    public Task Send(IEvent @event, params IEnumerable<IEntity> entities) => Send(new SendEventCommand {
        Event = @event,
        Entities = entities as IEntity[] ?? entities.ToArray()
    });

    public Task Share(IEntity entity) => entity.Share(this);

    public async Task ShareIfUnshared(IEntity entity) {
        if (!SharedEntities.Contains(entity))
            await Share(entity);
    }

    public Task Unshare(IEntity entity) => entity.Unshare(this);

    public async Task UnshareIfShared(IEntity entity) {
        if (SharedEntities.Contains(entity))
            await Unshare(entity);
    }

    public void Schedule(TimeSpan delay, Func<Task> action) =>
        DelayedTasks.Add(new DelayedTask(DateTimeOffset.UtcNow + delay, action));

    public void Schedule(DateTimeOffset time, Func<Task> action) =>
        DelayedTasks.Add(new DelayedTask(time, action));

    public virtual async Task Tick() {
        if (PingSendTime.AddSeconds(5) <= DateTimeOffset.UtcNow) {
            await Send(new PingEvent(DateTimeOffset.UtcNow));
            PingSendTime = DateTimeOffset.UtcNow;
        }

        foreach (DelayedTask delayedTask in DelayedTasks.Where(delayedTask => delayedTask.InvokeAtTime <= DateTimeOffset.UtcNow)) {
            await delayedTask.Task();
            DelayedTasks.TryRemove(delayedTask);
        }
    }

    public override int GetHashCode() => Id;

    [SuppressMessage("ReSharper", "ConditionalAccessQualifierIsNonNullableAccordingToAPIContract")]
    public override string ToString() =>
        $"PlayerConnection {{ " + $"ClientSession Id: '{ClientSession?.Id}'; " + $"Username: '{Player?.Username}' }}";

    public abstract void Dispose();

    public abstract ValueTask DisposeAsync();
}

public class SocketPlayerConnection(
    int id,
    IServiceScope serviceScope,
    Socket socket
) : PlayerConnection(id, serviceScope.ServiceProvider) {
    public IPEndPoint EndPoint { get; } = (IPEndPoint)socket.RemoteEndPoint!;

    public override bool IsLoggedIn => IsConnected && IsSocketConnected && ClientSession != null! && UserContainer != null! && Player != null!;
    bool IsSocketConnected => Socket.Connected;
    bool IsConnected { get; set; }

    Socket Socket { get; } = socket;
    Protocol.Protocol Protocol { get; } = serviceScope.ServiceProvider.GetRequiredService<Protocol.Protocol>();
    GameServer Server { get; } = serviceScope.ServiceProvider.GetRequiredService<GameServer>();

    public override async Task SetUsername(string username) {
        await base.SetUsername(username);
        Logger = Logger.WithPlayer(this);
    }

    public override async Task Kick(string? reason) {
        Logger.Warning("Player kicked (reason: '{Reason}')", reason);
        await Disconnect();
    }

    public async Task OnConnected() {
        Logger = Logger.WithEndPoint(EndPoint);

        ClientSession = new ClientSessionTemplate().Create();
        Logger.Information("New socket connected ({Id})", Id);

        _ = Task.Run(ReceiveAndExecute);

        await Send(new InitTimeCommand(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        await Share(ClientSession);

        IsConnected = true;
    }

    public override async Task Send(ICommand command) {
        if (!IsSocketConnected)
            return;

        try {
            Logger.Verbose("Encoding {Command}", command);

            await using ProtocolBuffer buffer = new(this);

            Protocol
                .GetCodec(new TypeCodecInfo(typeof(ICommand)))
                .Encode(buffer, command);

            using MemoryStream stream = new();
            await using BinaryWriter writer = new BigEndianBinaryWriter(stream);
            buffer.Wrap(writer);

            byte[] bytes = stream.ToArray();
            await Socket.SendAsync(bytes);

            Logger.Verbose("Sent {Command}: {Size} bytes ({Hex})", command, bytes.Length, Convert.ToHexString(bytes));
        } catch (Exception e) {
            Logger.Error(e, "Failed to send {Command}", command);
        }
    }

    async Task Disconnect() {
        if (!IsConnected) return;

        try {
            Socket.Shutdown(SocketShutdown.Both);
        } finally {
            Socket.Close();
            await OnDisconnected();
        }
    }

    async Task OnDisconnected() {
        if (!IsConnected) return;

        IsConnected = false;
        Logger.Information("Socket disconnected");

        try {
            if (UserContainer != null!) {
                await UserContainer.RemoveConnection(this);
                await UserContainer.Entity.RemoveComponent<UserOnlineComponent>();

                foreach (DiscordLinkRequest request in ConfigManager.DiscordLinkRequests.Where(dLinkReq => dLinkReq.UserId == UserContainer.Id))
                    ConfigManager.DiscordLinkRequests.TryRemove(request);
            }

            if (InLobby) {
                LobbyBase lobby = LobbyPlayer!.Lobby;

                if (LobbyPlayer.InRound) {
                    Round round = LobbyPlayer.Round;
                    await round.RemoveTanker(LobbyPlayer.Tanker);
                }

                await lobby.RemovePlayer(LobbyPlayer);
            }

            if (Spectating) {
                Round round = Spectator!.Round;
                await round.RemoveSpectator(Spectator);
            }
        } catch (Exception e) {
            Logger.Error(e, "Caught an exception while disconnecting socket");
        } finally {
            Server.RemovePlayer(Id);

            foreach (IEntity entity in SharedEntities)
                entity.SharedPlayers.TryRemove(this);

            SharedEntities.Clear();
        }

        await DisposeAsync();
    }

    public override async Task Tick() {
        if (!IsSocketConnected) {
            await Kick("Zombie");
            return;
        }

        await base.Tick();
    }

    async Task ReceiveAndExecute() {
        if (!IsSocketConnected)
            return;

        try {
            await using NetworkStream stream = new(Socket, FileAccess.Read);
            using BinaryReader reader = new BigEndianBinaryReader(stream);

            while (true) {
                await using ProtocolBuffer buffer = ProtocolBuffer.Unwrap(reader, this);
                long availableForRead = buffer.Stream.Length - buffer.Stream.Position;

                while (availableForRead > 0) {
                    Logger.Verbose("Decode buffer bytes available: {Count}", availableForRead);

                    IServerCommand command = (IServerCommand)Protocol
                        .GetCodec(new TypeCodecInfo(typeof(ICommand)))
                        .Decode(buffer);

                    try {
                        await command.Execute(this, ServiceProvider);
                    } catch (Exception e) {
                        Logger.Error(e, "Failed to execute {Command}", command);
                    }

                    availableForRead = buffer.Stream.Length - buffer.Stream.Position;
                }
            }
        } catch (Exception e) {
            Logger.Error(e, "Caught an exception while reading socket");
            await Disconnect();
            throw;
        }
    }

    public override void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public override async ValueTask DisposeAsync() {
        await DisposeAsyncCore();
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    void Dispose(bool disposing) {
        if (disposing) {
            Socket.Dispose();
            DelayedTasks.Clear();
            SharedEntities.Clear();
            serviceScope.Dispose();
        }
    }

    async ValueTask DisposeAsyncCore() {
        Socket.Dispose();
        DelayedTasks.Clear();
        SharedEntities.Clear();

        if (serviceScope is IAsyncDisposable ad)
            await ad.DisposeAsync();
        else serviceScope.Dispose();
    }

    ~SocketPlayerConnection() => Dispose(false);
}
