﻿using Vint.Core.Server.Game.Protocol.Codecs.Buffer;

namespace Vint.Core.Server.Game.Protocol.Codecs.Impl;

public class ULongCodec : Codec {
    public override void Encode(ProtocolBuffer buffer, object value) => buffer.Writer.Write((ulong)value);

    public override object Decode(ProtocolBuffer buffer) => buffer.Reader.ReadUInt64();
}
