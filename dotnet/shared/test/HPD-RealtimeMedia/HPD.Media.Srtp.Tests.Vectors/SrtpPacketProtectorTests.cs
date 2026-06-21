#nullable enable

using HPD.Events.Struct;
using HPD.Media.Diagnostics;
using HPD.Media.Transport;

namespace HPD.Media.Srtp.Tests.Vectors;

public sealed class SrtpPacketProtectorTests
{
    [Fact]
    public void KeyDerivation_MatchesRfc3711AppendixB3()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");

        byte[] cipherKey = AesCmSha1KeyDerivation.Derive(masterKey, masterSalt, 0x00, 16);
        byte[] cipherSalt = AesCmSha1KeyDerivation.Derive(masterKey, masterSalt, 0x02, 14);
        byte[] authKey = AesCmSha1KeyDerivation.Derive(masterKey, masterSalt, 0x01, 94);

        Assert.Equal(Convert.FromHexString("C61E7A93744F39EE10734AFE3FF7A087"), cipherKey);
        Assert.Equal(Convert.FromHexString("30CBBC08863D8C85D49DB34A9AE1"), cipherSalt);
        Assert.Equal(
            Convert.FromHexString(
                "CEBE321F6FF7716B6FD4AB49AF256A15" +
                "6D38BAA48F0A0ACF3C34E2359E6CDBCE" +
                "E049646C43D9327AD175578EF7227098" +
                "6371C10C9A369AC2F94A8C5FBCDDDC25" +
                "6D6E919A48B610EF17C2041E47403576" +
                "6B68642C59BBFC2F34DB60DBDFB2"),
            authKey);
    }

    [Fact]
    public void ProtectRtp_EncryptsAuthenticatesAndRoundTrips()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 1, ssrc);
        byte[] plain = packet[..24].ToArray();

        PacketProtectionStatus protectStatus = outbound.Protect(packet, 24, out int protectedLength);
        byte[] protectedPacket = packet[..protectedLength].ToArray();
        PacketProtectionStatus unprotectStatus = inbound.Unprotect(packet, protectedLength, out int unprotectedLength);

        Assert.Equal(PacketProtectionStatus.Success, protectStatus);
        Assert.Equal(34, protectedLength);
        Assert.NotEqual(plain.AsSpan(12, 12).ToArray(), protectedPacket.AsSpan(12, 12).ToArray());
        Assert.Equal(PacketProtectionStatus.Success, unprotectStatus);
        Assert.Equal(24, unprotectedLength);
        Assert.Equal(plain, packet[..unprotectedLength].ToArray());
    }

    [Fact]
    public void UnprotectRtp_RejectsAuthenticationFailure()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 2, ssrc);
        _ = outbound.Protect(packet, 24, out int protectedLength);
        packet[16] ^= 0x40;

        PacketProtectionStatus status = inbound.Unprotect(packet, protectedLength, out int outputLength);

        Assert.Equal(PacketProtectionStatus.AuthenticationFailed, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void UnprotectRtp_EmitsStructTelemetryForAuthenticationFailure()
    {
        using var hub = new StructEventHub();
        using StructEventInbox<SrtpRejectSample> inbox = hub
            .Route<SrtpRejectSample>(RealtimeMediaTelemetry.RouteOptions)
            .CreateInbox(new StructEventInboxOptions { Capacity = 4 });
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt, telemetry: emitters);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 2, ssrc);
        _ = outbound.Protect(packet, 24, out int protectedLength);
        packet[16] ^= 0x40;

        PacketProtectionStatus status = inbound.Unprotect(packet, protectedLength, out int outputLength);

        Assert.Equal(PacketProtectionStatus.AuthenticationFailed, status);
        Assert.Equal(0, outputLength);
        Assert.True(inbox.TryRead(out SrtpRejectSample sample));
        Assert.Equal(ssrc, sample.Ssrc);
        Assert.Equal(SrtpRejectKind.AuthenticationFailed, sample.RejectKind);
        Assert.False(sample.IsRtcp);
    }

    [Fact]
    public void UnprotectRtp_RejectsReplay()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 3, ssrc);
        _ = outbound.Protect(packet, 24, out int protectedLength);
        byte[] protectedPacket = packet[..protectedLength].ToArray();
        _ = inbound.Unprotect(packet, protectedLength, out _);
        protectedPacket.CopyTo(packet);

        PacketProtectionStatus replayStatus = inbound.Unprotect(packet, protectedLength, out int outputLength);

        Assert.Equal(PacketProtectionStatus.ReplayRejected, replayStatus);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void UnprotectRtp_AcceptsSequenceRollover()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector outbound = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        ushort[] sequenceNumbers = [0xFFFE, 0xFFFF, 0x0000, 0x0001];
        Span<byte> packet = stackalloc byte[64];

        foreach (ushort sequenceNumber in sequenceNumbers)
        {
            WriteRtpPacket(packet, sequenceNumber, ssrc);
            byte[] plain = packet[..24].ToArray();

            PacketProtectionStatus protectStatus = outbound.Protect(packet, 24, out int protectedLength);
            PacketProtectionStatus unprotectStatus = inbound.Unprotect(packet, protectedLength, out int unprotectedLength);

            Assert.Equal(PacketProtectionStatus.Success, protectStatus);
            Assert.Equal(PacketProtectionStatus.Success, unprotectStatus);
            Assert.Equal(24, unprotectedLength);
            Assert.Equal(plain, packet[..unprotectedLength].ToArray());
        }
    }

    [Fact]
    public void UnprotectRtp_AcceptsDelayedPreRolloverPacketInsideReplayWindow()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector outbound = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);

        byte[] protectedFffe = ProtectRtpPacket(outbound, sequenceNumber: 0xFFFE, ssrc, out byte[] plainFffe);
        byte[] protectedFfff = ProtectRtpPacket(outbound, sequenceNumber: 0xFFFF, ssrc, out byte[] plainFfff);
        byte[] protectedZero = ProtectRtpPacket(outbound, sequenceNumber: 0x0000, ssrc, out byte[] plainZero);
        Span<byte> packet = stackalloc byte[64];

        protectedFffe.CopyTo(packet);
        Assert.Equal(PacketProtectionStatus.Success, inbound.Unprotect(packet, protectedFffe.Length, out int unprotectedFffeLength));
        Assert.Equal(plainFffe, packet[..unprotectedFffeLength].ToArray());

        protectedZero.CopyTo(packet);
        Assert.Equal(PacketProtectionStatus.Success, inbound.Unprotect(packet, protectedZero.Length, out int unprotectedZeroLength));
        Assert.Equal(plainZero, packet[..unprotectedZeroLength].ToArray());

        protectedFfff.CopyTo(packet);
        PacketProtectionStatus delayedStatus = inbound.Unprotect(packet, protectedFfff.Length, out int delayedLength);

        Assert.Equal(PacketProtectionStatus.Success, delayedStatus);
        Assert.Equal(24, delayedLength);
        Assert.Equal(plainFfff, packet[..delayedLength].ToArray());
    }

    [Fact]
    public void ProtectRtcp_AppendsIndexAndRoundTrips()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtcpReceiverReport(packet, ssrc);
        byte[] plain = packet[..8].ToArray();

        PacketProtectionStatus protectStatus = outbound.Protect(packet, 8, out int protectedLength);
        PacketProtectionStatus unprotectStatus = inbound.Unprotect(packet, protectedLength, out int unprotectedLength);

        Assert.Equal(PacketProtectionStatus.Success, protectStatus);
        Assert.Equal(22, protectedLength);
        Assert.Equal(PacketProtectionStatus.Success, unprotectStatus);
        Assert.Equal(8, unprotectedLength);
        Assert.Equal(plain, packet[..unprotectedLength].ToArray());
    }

    [Fact]
    public void ProtectRtp_Aes128CmHmacSha1_32_UsesFourByteAuthenticationTag()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt, SrtpProtectionProfile.Aes128CmHmacSha1_32);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 4, ssrc);

        PacketProtectionStatus protectStatus = outbound.Protect(packet, 24, out int protectedLength);
        PacketProtectionStatus unprotectStatus = inbound.Unprotect(packet, protectedLength, out int unprotectedLength);

        Assert.Equal(4, outbound.MaximumExpansionBytes);
        Assert.Equal(PacketProtectionStatus.Success, protectStatus);
        Assert.Equal(28, protectedLength);
        Assert.Equal(PacketProtectionStatus.Success, unprotectStatus);
        Assert.Equal(24, unprotectedLength);
    }

    [Fact]
    public void ProtectRtcp_Aes128CmHmacSha1_32_UsesIndexAndFourByteAuthenticationTag()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt, SrtpProtectionProfile.Aes128CmHmacSha1_32);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtcpReceiverReport(packet, ssrc);

        PacketProtectionStatus protectStatus = outbound.Protect(packet, 8, out int protectedLength);
        PacketProtectionStatus unprotectStatus = inbound.Unprotect(packet, protectedLength, out int unprotectedLength);

        Assert.Equal(8, outbound.MaximumExpansionBytes);
        Assert.Equal(PacketProtectionStatus.Success, protectStatus);
        Assert.Equal(16, protectedLength);
        Assert.Equal(PacketProtectionStatus.Success, unprotectStatus);
        Assert.Equal(8, unprotectedLength);
    }

    [Fact]
    public void ProtectRtp_AeadAes128Gcm_EncryptsAuthenticatesAndRoundTrips()
    {
        byte[] masterKey = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");
        byte[] masterSalt = Convert.FromHexString("517569642070726F2071756F");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt, SrtpProtectionProfile.AeadAes128Gcm);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[72];
        WriteRtpPacket(packet, sequenceNumber: 21, ssrc);
        byte[] plain = packet[..24].ToArray();

        PacketProtectionStatus protectStatus = outbound.Protect(packet, 24, out int protectedLength);
        byte[] protectedPacket = packet[..protectedLength].ToArray();
        PacketProtectionStatus unprotectStatus = inbound.Unprotect(packet, protectedLength, out int unprotectedLength);

        Assert.Equal(16, outbound.MaximumExpansionBytes);
        Assert.Equal(PacketProtectionStatus.Success, protectStatus);
        Assert.Equal(40, protectedLength);
        Assert.NotEqual(plain.AsSpan(12, 12).ToArray(), protectedPacket.AsSpan(12, 12).ToArray());
        Assert.Equal(PacketProtectionStatus.Success, unprotectStatus);
        Assert.Equal(24, unprotectedLength);
        Assert.Equal(plain, packet[..unprotectedLength].ToArray());
    }

    [Fact]
    public void ProtectRtcp_AeadAes128Gcm_AppendsTagAndIndexThenRoundTrips()
    {
        byte[] masterKey = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");
        byte[] masterSalt = Convert.FromHexString("517569642070726F2071756F");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt, SrtpProtectionProfile.AeadAes128Gcm);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[72];
        WriteRtcpReceiverReport(packet, ssrc);
        byte[] plain = packet[..8].ToArray();

        PacketProtectionStatus protectStatus = outbound.Protect(packet, 8, out int protectedLength);
        PacketProtectionStatus unprotectStatus = inbound.Unprotect(packet, protectedLength, out int unprotectedLength);

        Assert.Equal(20, outbound.MaximumExpansionBytes);
        Assert.Equal(PacketProtectionStatus.Success, protectStatus);
        Assert.Equal(28, protectedLength);
        Assert.Equal(PacketProtectionStatus.Success, unprotectStatus);
        Assert.Equal(8, unprotectedLength);
        Assert.Equal(plain, packet[..unprotectedLength].ToArray());
    }

    [Fact]
    public void UnprotectRtp_AeadAes128Gcm_RejectsAuthenticationFailure()
    {
        byte[] masterKey = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");
        byte[] masterSalt = Convert.FromHexString("517569642070726F2071756F");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt, SrtpProtectionProfile.AeadAes128Gcm);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[72];
        WriteRtpPacket(packet, sequenceNumber: 22, ssrc);
        _ = outbound.Protect(packet, 24, out int protectedLength);
        packet[16] ^= 0x40;

        PacketProtectionStatus status = inbound.Unprotect(packet, protectedLength, out int outputLength);

        Assert.Equal(PacketProtectionStatus.AuthenticationFailed, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void UnprotectRtcp_AeadAes128Gcm_RejectsReplay()
    {
        byte[] masterKey = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");
        byte[] masterSalt = Convert.FromHexString("517569642070726F2071756F");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt, SrtpProtectionProfile.AeadAes128Gcm);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[72];
        WriteRtcpReceiverReport(packet, ssrc);
        _ = outbound.Protect(packet, 8, out int protectedLength);
        byte[] protectedPacket = packet[..protectedLength].ToArray();
        _ = inbound.Unprotect(packet, protectedLength, out _);
        protectedPacket.CopyTo(packet);

        PacketProtectionStatus replayStatus = inbound.Unprotect(packet, protectedLength, out int outputLength);

        Assert.Equal(PacketProtectionStatus.ReplayRejected, replayStatus);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void ProtectRtp_WithMki_AppendsMkiAndRoundTrips()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        byte[] mki = [0xA1, 0xB2, 0xC3];
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt, mki: mki, allowMki: true);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 5, ssrc);
        byte[] plain = packet[..24].ToArray();

        PacketProtectionStatus protectStatus = outbound.Protect(packet, 24, out int protectedLength);
        PacketProtectionStatus unprotectStatus = inbound.Unprotect(packet, protectedLength, out int unprotectedLength);

        Assert.Equal(13, outbound.MaximumExpansionBytes);
        Assert.Equal(PacketProtectionStatus.Success, protectStatus);
        Assert.Equal(37, protectedLength);
        Assert.Equal(mki, packet.Slice(24, mki.Length).ToArray());
        Assert.Equal(PacketProtectionStatus.Success, unprotectStatus);
        Assert.Equal(24, unprotectedLength);
        Assert.Equal(plain, packet[..unprotectedLength].ToArray());
    }

    [Fact]
    public void UnprotectRtp_WithMki_RejectsMkiMismatch()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        byte[] mki = [0xA1, 0xB2, 0xC3];
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt, mki: mki, allowMki: true);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 6, ssrc);
        _ = outbound.Protect(packet, 24, out int protectedLength);
        packet[24] ^= 0x01;

        PacketProtectionStatus status = inbound.Unprotect(packet, protectedLength, out int outputLength);

        Assert.Equal(PacketProtectionStatus.AuthenticationFailed, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void ProtectRtcp_WithMki_AppendsMkiAndRoundTrips()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        byte[] mki = [0xA1, 0xB2, 0xC3];
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt, mki: mki, allowMki: true);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtcpReceiverReport(packet, ssrc);
        byte[] plain = packet[..8].ToArray();

        PacketProtectionStatus protectStatus = outbound.Protect(packet, 8, out int protectedLength);
        PacketProtectionStatus unprotectStatus = inbound.Unprotect(packet, protectedLength, out int unprotectedLength);

        Assert.Equal(17, outbound.MaximumExpansionBytes);
        Assert.Equal(PacketProtectionStatus.Success, protectStatus);
        Assert.Equal(25, protectedLength);
        Assert.Equal(mki, packet.Slice(12, mki.Length).ToArray());
        Assert.Equal(PacketProtectionStatus.Success, unprotectStatus);
        Assert.Equal(8, unprotectedLength);
        Assert.Equal(plain, packet[..unprotectedLength].ToArray());
    }

    [Fact]
    public void Protect_WithMkiWhenDisallowed_ReturnsUnsupportedProfile()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        byte[] mki = [0xA1, 0xB2, 0xC3];
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt, mki: mki, allowMki: false)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 7, ssrc);

        PacketProtectionStatus status = protector.Protect(packet, 24, out int outputLength);

        Assert.Equal(0, protector.MaximumExpansionBytes);
        Assert.Equal(PacketProtectionStatus.UnsupportedProfile, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void Create_WithInvalidMasterKeyLength_ReturnsUnsupportedProtector()
    {
        byte[] masterKey = new byte[15];
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 8, ssrc);

        PacketProtectionStatus status = protector.Protect(packet, 24, out int outputLength);

        Assert.Equal(0, protector.MaximumExpansionBytes);
        Assert.Equal(PacketProtectionStatus.UnsupportedProfile, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void Create_WithInvalidMasterSaltLength_ReturnsUnsupportedProtector()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = new byte[13];
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtcpReceiverReport(packet, ssrc);

        PacketProtectionStatus status = protector.Protect(packet, 8, out int outputLength);

        Assert.Equal(0, protector.MaximumExpansionBytes);
        Assert.Equal(PacketProtectionStatus.UnsupportedProfile, status);
        Assert.Equal(0, outputLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void Create_WithInvalidReplayWindowSize_ReturnsUnsupportedProtector(int replayWindowSize)
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt, replayWindowSize: replayWindowSize)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 9, ssrc);

        PacketProtectionStatus status = protector.Protect(packet, 24, out int outputLength);

        Assert.Equal(0, protector.MaximumExpansionBytes);
        Assert.Equal(PacketProtectionStatus.UnsupportedProfile, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void Create_WithUnknownPurposeReturnsUnsupportedProtector()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create((PacketProtectionPurpose)99, PacketDirection.Outbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 10, ssrc);

        PacketProtectionStatus status = protector.Protect(packet, 24, out int outputLength);

        Assert.Equal(0, protector.MaximumExpansionBytes);
        Assert.Equal(PacketProtectionStatus.UnsupportedProfile, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void Create_WithUnknownDirectionReturnsUnsupportedProtector()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, (PacketDirection)99, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 11, ssrc);

        PacketProtectionStatus status = protector.Protect(packet, 24, out int outputLength);

        Assert.Equal(0, protector.MaximumExpansionBytes);
        Assert.Equal(PacketProtectionStatus.UnsupportedProfile, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void UnprotectRtcp_RejectsAuthenticationFailure()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtcpReceiverReport(packet, ssrc);
        _ = outbound.Protect(packet, 8, out int protectedLength);
        packet[8] ^= 0x40;

        PacketProtectionStatus status = inbound.Unprotect(packet, protectedLength, out int outputLength);

        Assert.Equal(PacketProtectionStatus.AuthenticationFailed, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void UnprotectRtcp_RejectsReplay()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtcpReceiverReport(packet, ssrc);
        _ = outbound.Protect(packet, 8, out int protectedLength);
        byte[] protectedPacket = packet[..protectedLength].ToArray();
        _ = inbound.Unprotect(packet, protectedLength, out _);
        protectedPacket.CopyTo(packet);

        PacketProtectionStatus replayStatus = inbound.Unprotect(packet, protectedLength, out int outputLength);

        Assert.Equal(PacketProtectionStatus.ReplayRejected, replayStatus);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void UnprotectRtcp_EmitsStructTelemetryForReplayReject()
    {
        using var hub = new StructEventHub();
        using StructEventInbox<SrtpRejectSample> inbox = hub
            .Route<SrtpRejectSample>(RealtimeMediaTelemetry.RouteOptions)
            .CreateInbox(new StructEventInboxOptions { Capacity = 4 });
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt, telemetry: emitters);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtcp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtcpReceiverReport(packet, ssrc);
        _ = outbound.Protect(packet, 8, out int protectedLength);
        byte[] protectedPacket = packet[..protectedLength].ToArray();
        _ = inbound.Unprotect(packet, protectedLength, out _);
        protectedPacket.CopyTo(packet);

        PacketProtectionStatus replayStatus = inbound.Unprotect(packet, protectedLength, out int outputLength);

        Assert.Equal(PacketProtectionStatus.ReplayRejected, replayStatus);
        Assert.Equal(0, outputLength);
        Assert.True(inbox.TryRead(out SrtpRejectSample sample));
        Assert.Equal(ssrc, sample.Ssrc);
        Assert.Equal(SrtpRejectKind.ReplayRejected, sample.RejectKind);
        Assert.True(sample.IsRtcp);
    }

    [Fact]
    public void ProtectRtp_ReturnsDestinationTooSmall()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        Span<byte> packet = stackalloc byte[24];
        WriteRtpPacket(packet, sequenceNumber: 4, ssrc);

        PacketProtectionStatus status = protector.Protect(packet, 24, out int outputLength);

        Assert.Equal(PacketProtectionStatus.DestinationTooSmall, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void ProtectRtp_ReturnsInvalidPacketWhenInputLengthExceedsBuffer()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        Span<byte> packet = stackalloc byte[24];
        WriteRtpPacket(packet, sequenceNumber: 8, ssrc);

        PacketProtectionStatus status = protector.Protect(packet, 25, out int outputLength);

        Assert.Equal(PacketProtectionStatus.InvalidPacket, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void ProtectRtp_ReturnsInvalidPacketForUnsupportedVersion()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 12, ssrc);
        packet[0] = 0x40;

        PacketProtectionStatus status = protector.Protect(packet, 24, out int outputLength);

        Assert.Equal(PacketProtectionStatus.InvalidPacket, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void UnprotectRtp_ReturnsInvalidPacketForUnsupportedVersion()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 13, ssrc);
        packet[0] = 0x40;

        PacketProtectionStatus status = protector.Unprotect(packet, 34, out int outputLength);

        Assert.Equal(PacketProtectionStatus.InvalidPacket, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void ProtectRtcp_ReturnsInvalidPacketForUnsupportedVersion()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtcpReceiverReport(packet, ssrc);
        packet[0] = 0x40;

        PacketProtectionStatus status = protector.Protect(packet, 8, out int outputLength);

        Assert.Equal(PacketProtectionStatus.InvalidPacket, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void UnprotectRtcp_ReturnsInvalidPacketForUnsupportedVersion()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtcp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtcpReceiverReport(packet, ssrc);
        packet[0] = 0x40;

        PacketProtectionStatus status = protector.Unprotect(packet, 22, out int outputLength);

        Assert.Equal(PacketProtectionStatus.InvalidPacket, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void ProtectRtp_ReturnsInvalidPacketWithoutMutationWhenAuthenticationInputIsTooLarge()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        byte[] packet = new byte[65_546];
        WriteRtpPacket(packet, sequenceNumber: 8, ssrc);
        byte[] before = packet.ToArray();

        PacketProtectionStatus status = protector.Protect(packet, inputLength: 65_536, out int outputLength);

        Assert.Equal(PacketProtectionStatus.InvalidPacket, status);
        Assert.Equal(0, outputLength);
        Assert.Equal(before, packet);
    }

    [Fact]
    public void ProtectRtp_DoesNotAdvanceRolloverStateForMalformedPacket()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber: 0xFFFE, ssrc);
        packet[0] |= 0x10;
        packet[14] = 0x00;
        packet[15] = 0x01;

        PacketProtectionStatus malformedStatus = outbound.Protect(packet, inputLength: 16, out int malformedOutputLength);

        WriteRtpPacket(packet, sequenceNumber: 1, ssrc);
        PacketProtectionStatus protectStatus = outbound.Protect(packet, inputLength: 24, out int protectedLength);
        PacketProtectionStatus unprotectStatus = inbound.Unprotect(packet, protectedLength, out int unprotectedLength);

        Assert.Equal(PacketProtectionStatus.InvalidPacket, malformedStatus);
        Assert.Equal(0, malformedOutputLength);
        Assert.Equal(PacketProtectionStatus.Success, protectStatus);
        Assert.Equal(PacketProtectionStatus.Success, unprotectStatus);
        Assert.Equal(24, unprotectedLength);
    }

    [Fact]
    public void ProtectRtcp_ReturnsInvalidPacketWithoutMutationWhenAuthenticationInputIsTooLarge()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        byte[] packet = new byte[65_554];
        WriteRtcpReceiverReport(packet, ssrc);
        byte[] before = packet.ToArray();

        PacketProtectionStatus status = protector.Protect(packet, inputLength: 65_540, out int outputLength);

        Assert.Equal(PacketProtectionStatus.InvalidPacket, status);
        Assert.Equal(0, outputLength);
        Assert.Equal(before, packet);
    }

    [Fact]
    public void UnprotectRtp_ReturnsInvalidPacketWhenInputLengthExceedsBuffer()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[24];
        WriteRtpPacket(packet, sequenceNumber: 9, ssrc);

        PacketProtectionStatus status = protector.Unprotect(packet, 25, out int outputLength);

        Assert.Equal(PacketProtectionStatus.InvalidPacket, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void UnprotectRtcp_ReturnsInvalidPacketWhenInputLengthExceedsBuffer()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtcp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[16];
        WriteRtcpReceiverReport(packet, ssrc);

        PacketProtectionStatus status = protector.Unprotect(packet, 17, out int outputLength);

        Assert.Equal(PacketProtectionStatus.InvalidPacket, status);
        Assert.Equal(0, outputLength);
    }

    [Fact]
    public void ProtectRtp_DoesNotAllocateAfterWarmup()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        Span<byte> packet = stackalloc byte[64];

        for (ushort sequence = 1; sequence <= 8; sequence++)
        {
            WriteRtpPacket(packet, sequence, ssrc);
            PacketProtectionStatus status = protector.Protect(packet, 24, out _);
            Assert.Equal(PacketProtectionStatus.Success, status);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ushort sequence = 9; sequence < 109; sequence++)
        {
            WriteRtpPacket(packet, sequence, ssrc);
            if (protector.Protect(packet, 24, out _) != PacketProtectionStatus.Success)
            {
                throw new InvalidOperationException("SRTP protect failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void UnprotectRtp_DoesNotAllocateAfterWarmup()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector outbound = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        byte[][] protectedPackets = new byte[108][];
        Span<byte> packet = stackalloc byte[64];

        for (ushort sequence = 1; sequence <= protectedPackets.Length; sequence++)
        {
            WriteRtpPacket(packet, sequence, ssrc);
            Assert.Equal(PacketProtectionStatus.Success, outbound.Protect(packet, 24, out int protectedLength));
            protectedPackets[sequence - 1] = packet[..protectedLength].ToArray();
        }

        for (int i = 0; i < 8; i++)
        {
            protectedPackets[i].CopyTo(packet);
            PacketProtectionStatus status = inbound.Unprotect(packet, protectedPackets[i].Length, out _);
            Assert.Equal(PacketProtectionStatus.Success, status);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 8; i < protectedPackets.Length; i++)
        {
            protectedPackets[i].CopyTo(packet);
            if (inbound.Unprotect(packet, protectedPackets[i].Length, out _) != PacketProtectionStatus.Success)
            {
                throw new InvalidOperationException("SRTP unprotect failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ProtectRtcp_DoesNotAllocateAfterWarmup()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        Span<byte> packet = stackalloc byte[64];

        for (int i = 0; i < 8; i++)
        {
            WriteRtcpReceiverReport(packet, ssrc);
            PacketProtectionStatus status = protector.Protect(packet, 8, out _);
            Assert.Equal(PacketProtectionStatus.Success, status);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            WriteRtcpReceiverReport(packet, ssrc);
            if (protector.Protect(packet, 8, out _) != PacketProtectionStatus.Success)
            {
                throw new InvalidOperationException("SRTCP protect failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void UnprotectRtcp_DoesNotAllocateAfterWarmup()
    {
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtector outbound = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = CreateFactory(masterKey, masterSalt)
            .Create(PacketProtectionPurpose.Rtcp, PacketDirection.Inbound, ssrc);
        byte[][] protectedPackets = new byte[108][];
        Span<byte> packet = stackalloc byte[64];

        for (int i = 0; i < protectedPackets.Length; i++)
        {
            WriteRtcpReceiverReport(packet, ssrc);
            Assert.Equal(PacketProtectionStatus.Success, outbound.Protect(packet, 8, out int protectedLength));
            protectedPackets[i] = packet[..protectedLength].ToArray();
        }

        for (int i = 0; i < 8; i++)
        {
            protectedPackets[i].CopyTo(packet);
            PacketProtectionStatus status = inbound.Unprotect(packet, protectedPackets[i].Length, out _);
            Assert.Equal(PacketProtectionStatus.Success, status);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 8; i < protectedPackets.Length; i++)
        {
            protectedPackets[i].CopyTo(packet);
            if (inbound.Unprotect(packet, protectedPackets[i].Length, out _) != PacketProtectionStatus.Success)
            {
                throw new InvalidOperationException("SRTCP unprotect failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void UnprotectRtp_TelemetryWithNoSubscribersDoesNotAllocateAfterWarmup()
    {
        using var hub = new StructEventHub();
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
        byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
        uint ssrc = 0x11223344;
        IPacketProtectorFactory factory = CreateFactory(masterKey, masterSalt, telemetry: emitters);
        IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);
        Span<byte> packet = stackalloc byte[64];

        for (ushort sequence = 1; sequence <= 8; sequence++)
        {
            WriteRtpPacket(packet, sequence, ssrc);
            Assert.Equal(PacketProtectionStatus.Success, outbound.Protect(packet, 24, out int protectedLength));
            packet[16] ^= 0x40;
            Assert.Equal(PacketProtectionStatus.AuthenticationFailed, inbound.Unprotect(packet, protectedLength, out _));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ushort sequence = 9; sequence < 109; sequence++)
        {
            WriteRtpPacket(packet, sequence, ssrc);
            if (outbound.Protect(packet, 24, out int protectedLength) != PacketProtectionStatus.Success)
            {
                throw new InvalidOperationException("SRTP protect failed during allocation measurement.");
            }

            packet[16] ^= 0x40;
            if (inbound.Unprotect(packet, protectedLength, out _) != PacketProtectionStatus.AuthenticationFailed)
            {
                throw new InvalidOperationException("SRTP reject telemetry path failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ProtectRtp_AeadAes128Gcm_DoesNotAllocateAfterWarmup()
    {
        byte[] masterKey = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");
        byte[] masterSalt = Convert.FromHexString("517569642070726F2071756F");
        uint ssrc = 0x11223344;
        IPacketProtector protector = CreateFactory(masterKey, masterSalt, SrtpProtectionProfile.AeadAes128Gcm)
            .Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
        Span<byte> packet = stackalloc byte[72];

        for (ushort sequence = 1; sequence <= 8; sequence++)
        {
            WriteRtpPacket(packet, sequence, ssrc);
            PacketProtectionStatus status = protector.Protect(packet, 24, out _);
            Assert.Equal(PacketProtectionStatus.Success, status);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ushort sequence = 9; sequence < 109; sequence++)
        {
            WriteRtpPacket(packet, sequence, ssrc);
            if (protector.Protect(packet, 24, out _) != PacketProtectionStatus.Success)
            {
                throw new InvalidOperationException("AEAD SRTP protect failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void UnprotectRtcp_AeadAes128Gcm_DoesNotAllocateAfterWarmup()
    {
        byte[] masterKey = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");
        byte[] masterSalt = Convert.FromHexString("517569642070726F2071756F");
        uint ssrc = 0x11223344;
        IPacketProtector outbound = CreateFactory(masterKey, masterSalt, SrtpProtectionProfile.AeadAes128Gcm)
            .Create(PacketProtectionPurpose.Rtcp, PacketDirection.Outbound, ssrc);
        IPacketProtector inbound = CreateFactory(masterKey, masterSalt, SrtpProtectionProfile.AeadAes128Gcm)
            .Create(PacketProtectionPurpose.Rtcp, PacketDirection.Inbound, ssrc);
        byte[][] protectedPackets = new byte[108][];
        Span<byte> packet = stackalloc byte[72];

        for (int i = 0; i < protectedPackets.Length; i++)
        {
            WriteRtcpReceiverReport(packet, ssrc);
            Assert.Equal(PacketProtectionStatus.Success, outbound.Protect(packet, 8, out int protectedLength));
            protectedPackets[i] = packet[..protectedLength].ToArray();
        }

        for (int i = 0; i < 8; i++)
        {
            protectedPackets[i].CopyTo(packet);
            PacketProtectionStatus status = inbound.Unprotect(packet, protectedPackets[i].Length, out _);
            Assert.Equal(PacketProtectionStatus.Success, status);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 8; i < protectedPackets.Length; i++)
        {
            protectedPackets[i].CopyTo(packet);
            if (inbound.Unprotect(packet, protectedPackets[i].Length, out _) != PacketProtectionStatus.Success)
            {
                throw new InvalidOperationException("AEAD SRTCP unprotect failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    private static IPacketProtectorFactory CreateFactory(
        byte[] masterKey,
        byte[] masterSalt,
        SrtpProtectionProfile profile = SrtpProtectionProfile.Aes128CmHmacSha1_80,
        byte[]? mki = null,
        bool allowMki = false,
        int replayWindowSize = 64,
        RealtimeMediaTelemetryEmitters? telemetry = null)
    {
        var material = new SrtpProtectionMaterial
        {
            Profile = profile,
            OutboundMasterKey = masterKey,
            OutboundMasterSalt = masterSalt,
            InboundMasterKey = masterKey,
            InboundMasterSalt = masterSalt,
            Mki = mki
        };

        var options = new SrtpPacketProtectionOptions
        {
            AllowMki = allowMki,
            ReplayWindowSize = replayWindowSize
        };
        var builder = new AesCmSha1SrtpPacketProtectorFactoryBuilder();
        return telemetry is { } emitters
            ? builder.Create(material, options, emitters)
            : builder.Create(material, options);
    }

    private static void WriteRtpPacket(Span<byte> packet, ushort sequenceNumber, uint ssrc)
    {
        packet.Clear();
        packet[0] = 0x80;
        packet[1] = 0x60;
        packet[2] = (byte)(sequenceNumber >> 8);
        packet[3] = (byte)sequenceNumber;
        packet[4] = 0x01;
        packet[5] = 0x02;
        packet[6] = 0x03;
        packet[7] = 0x04;
        packet[8] = (byte)(ssrc >> 24);
        packet[9] = (byte)(ssrc >> 16);
        packet[10] = (byte)(ssrc >> 8);
        packet[11] = (byte)ssrc;

        for (int i = 12; i < 24; i++)
        {
            packet[i] = (byte)i;
        }
    }

    private static byte[] ProtectRtpPacket(
        IPacketProtector protector,
        ushort sequenceNumber,
        uint ssrc,
        out byte[] plain)
    {
        Span<byte> packet = stackalloc byte[64];
        WriteRtpPacket(packet, sequenceNumber, ssrc);
        plain = packet[..24].ToArray();
        Assert.Equal(PacketProtectionStatus.Success, protector.Protect(packet, 24, out int protectedLength));
        return packet[..protectedLength].ToArray();
    }

    private static void WriteRtcpReceiverReport(Span<byte> packet, uint ssrc)
    {
        packet.Clear();
        packet[0] = 0x80;
        packet[1] = 0xC9;
        packet[2] = 0x00;
        packet[3] = 0x01;
        packet[4] = (byte)(ssrc >> 24);
        packet[5] = (byte)(ssrc >> 16);
        packet[6] = (byte)(ssrc >> 8);
        packet[7] = (byte)ssrc;
    }
}
