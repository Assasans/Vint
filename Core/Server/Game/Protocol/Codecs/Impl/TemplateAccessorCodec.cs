﻿using Vint.Core.ECS.Templates;
using Vint.Core.Server.Game.Protocol.Codecs.Buffer;
using Vint.Core.Utils;

namespace Vint.Core.Server.Game.Protocol.Codecs.Impl;

public class TemplateAccessorCodec : Codec {
    public override void Encode(ProtocolBuffer buffer, object value) {
        if (value is not TemplateAccessor templateAccessor)
            throw new ArgumentException("Value must be TemplateAccessor");

        buffer.Writer.Write(templateAccessor.Template.GetType().GetProtocolId());

        Protocol
            .GetCodec(new TypeCodecInfo(typeof(string), true))
            .Encode(buffer, templateAccessor.ConfigPath!);
    }

    public override object Decode(ProtocolBuffer buffer) => throw new NotSupportedException();
}
