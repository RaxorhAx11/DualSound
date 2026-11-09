using DualSound.Audio;
using DualSound.Contracts.Enums;
using Xunit;

namespace DualSound.Tests;

public class AudioDeviceClassifierTests
{
    [Fact]
    public void Classifies_HdmiDisplayDevice_When_FormFactorIs9()
    {
        var (type, desc) = AudioDeviceClassifier.Classify(
            formFactor: 9,
            enumeratorName: "HDAUDIO",
            instanceId: @"{1}.HDAUDIO\FUNC_01&VEN_1002&DEV_AA01",
            interfacePath: @"e0hdmiout2wave",
            adapterName: "AMD High Definition Audio Device");

        Assert.Equal(AudioDeviceType.HdmiDisplayPort, type);
        Assert.Equal("HDMI / DisplayPort Audio", desc);
    }

    [Fact]
    public void Classifies_Bluetooth_When_EnumeratorIsBthEnum()
    {
        var (type, desc) = AudioDeviceClassifier.Classify(
            formFactor: 3,
            enumeratorName: "BTHENUM",
            instanceId: @"{1}.BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}",
            interfacePath: @"src",
            adapterName: "realme Buds T310");

        Assert.Equal(AudioDeviceType.BluetoothAudio, type);
        Assert.Equal("Bluetooth Audio (Headphones)", desc);
    }

    [Fact]
    public void Classifies_UsbAudio_When_EnumeratorIsUsb()
    {
        var (type, desc) = AudioDeviceClassifier.Classify(
            formFactor: 1,
            enumeratorName: "USB",
            instanceId: @"{1}.USB\VID_1B80&PID_B50C&MI_00\7",
            interfacePath: @"spkout00wave",
            adapterName: "GAMDIAS USB Audio Device");

        Assert.Equal(AudioDeviceType.UsbAudio, type);
        Assert.Equal("USB Audio (Speakers)", desc);
    }

    [Fact]
    public void Classifies_35mmHeadphones_When_HDAudioAndHeadphoneFormFactor()
    {
        var (type, desc) = AudioDeviceClassifier.Classify(
            formFactor: 3,
            enumeratorName: "HDAUDIO",
            instanceId: @"{1}.HDAUDIO\FUNC_01&VEN_10EC&DEV_0294",
            interfacePath: @"rearlineoutwavehap3_a2b1",
            adapterName: "Realtek(R) Audio");

        Assert.Equal(AudioDeviceType.Headphones, type);
        Assert.Equal("3.5 mm Audio (Headphone Jack)", desc);
    }

    [Fact]
    public void Classifies_35mmLineOut_When_HDAudioAndLineLevelFormFactor()
    {
        var (type, desc) = AudioDeviceClassifier.Classify(
            formFactor: 2,
            enumeratorName: "HDAUDIO",
            instanceId: @"{1}.HDAUDIO\FUNC_01&VEN_10EC&DEV_0294",
            interfacePath: @"rearlineoutwavehap_a2b1",
            adapterName: "Realtek(R) Audio");

        Assert.Equal(AudioDeviceType.LineOut35mm, type);
        Assert.Equal("3.5 mm Audio (Line Out)", desc);
    }

    [Fact]
    public void Classifies_VirtualAudio_When_EnumeratorIsRootOrSwd()
    {
        var (type, desc) = AudioDeviceClassifier.Classify(
            formFactor: 5,
            enumeratorName: "ROOT",
            instanceId: @"{1}.ROOT\MEDIA\0001",
            interfacePath: @"chat_wave_speaker",
            adapterName: "SteelSeries Sonar Virtual Audio Device");

        Assert.Equal(AudioDeviceType.VirtualAudio, type);
        Assert.Equal("Virtual Audio Device", desc);
    }

    [Fact]
    public void Classifies_UnknownConnectionType_When_MetadataIsUnverified()
    {
        var (type, desc) = AudioDeviceClassifier.Classify(
            formFactor: null,
            enumeratorName: null,
            instanceId: null,
            interfacePath: null,
            adapterName: null);

        Assert.Equal(AudioDeviceType.Unknown, type);
        Assert.Equal("Unknown connection type", desc);
    }

    [Fact]
    public void DoesNotAssume_RealtekIsAlways35mm_IfUsbCard()
    {
        // Realtek also manufactures USB DACs and audio dongles
        var (type, desc) = AudioDeviceClassifier.Classify(
            formFactor: 3,
            enumeratorName: "USB",
            instanceId: @"{1}.USB\VID_0BDA&PID_4014",
            interfacePath: @"spkout00wave",
            adapterName: "Realtek USB Audio");

        Assert.Equal(AudioDeviceType.UsbAudio, type);
        Assert.Equal("USB Audio (Headphones)", desc);
    }
}

