﻿namespace Vint.Core.Server.Game.Protocol.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class ProtocolIdAttribute(
    long id
) : Attribute {
    public long Id => id;

    public override string ToString() => $"ProtocolId {{ Id: {Id} }}";
}
