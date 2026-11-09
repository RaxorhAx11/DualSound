using System;
using NAudio.CoreAudioApi;

namespace DualSound.Audio;

/// <summary>
/// Windows CoreAudio Property System keys used to query endpoint metadata.
/// </summary>
public static class WindowsAudioPropertyKeys
{
    // PKEY_AudioEndpoint_FormFactor: {1da5d803-d492-4edd-8c23-e0c0ffee7f0e}, 0
    public static readonly PropertyKey FormFactor = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);

    // PKEY_Device_EnumeratorName: {a45c254e-df1c-4efd-8020-67d146a850e0}, 24
    public static readonly PropertyKey EnumeratorName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);

    // PKEY_Device_DeviceDesc: {a45c254e-df1c-4efd-8020-67d146a850e0}, 2
    public static readonly PropertyKey DeviceDesc = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);

    // PKEY_Device_FriendlyName: {a45c254e-df1c-4efd-8020-67d146a850e0}, 14
    public static readonly PropertyKey DeviceInterfaceFriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    // PKEY_Device_InstanceId: {b3f8fa53-0004-438e-9003-51a46e139bfc}, 2
    public static readonly PropertyKey DeviceInstanceId = new(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 2);

    // PKEY_DeviceInterface_FriendlyName: {026e516e-b814-414b-83cd-856d6fef4822}, 2
    public static readonly PropertyKey AdapterFriendlyName = new(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);

    // Device Interface Path: {233164c8-1b2c-4c7d-bc68-b671687a2567}, 1
    public static readonly PropertyKey InterfacePath = new(new Guid("233164c8-1b2c-4c7d-bc68-b671687a2567"), 1);
}

