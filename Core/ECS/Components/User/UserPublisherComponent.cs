﻿using Vint.Core.Server.Game.Protocol.Attributes;

namespace Vint.Core.ECS.Components.User;

[ProtocolId(32195187150433)]
public class UserPublisherComponent : PrivateComponent {
    public byte Publisher { get; private set; } = 0; // 0 - GLOBAL, 1 - CONSALA (for turkey)
}
