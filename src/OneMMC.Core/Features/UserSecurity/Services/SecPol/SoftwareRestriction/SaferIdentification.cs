using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.SoftwareRestriction;

/// <summary>
/// <c>SAFER_IDENTIFICATION_TYPES</c>: the rule kind carried in an identification header.
/// </summary>
internal enum SaferIdentificationType : uint
{
    /// <summary>SaferIdentityDefault; used with a GUID to query or delete a rule.</summary>
    Default = 0,

    /// <summary>SaferIdentityTypeImageName: a path or registry-path rule.</summary>
    ImageName = 1,

    /// <summary>SaferIdentityTypeImageHash: a file hash rule.</summary>
    ImageHash = 2,

    /// <summary>SaferIdentityTypeUrlZone: an Internet zone rule.</summary>
    UrlZone = 3
}

/// <summary>
/// Managed form of the <c>SAFER_PATHNAME_IDENTIFICATION</c>, <c>SAFER_HASH_IDENTIFICATION2</c> and
/// <c>SAFER_URLZONE_IDENTIFICATION</c> structures exchanged with <c>Safer{Get,Set}LevelInformation</c>
/// (<c>SaferObjectSingleIdentification</c>).
/// </summary>
/// <remarks>
/// The structures are serialized by explicit offset instead of CsWin32's fixed-buffer structs so the
/// path rule's embedded <c>ImageName</c> pointer and the variable trailing data can be handled directly.
/// Offsets follow winsafer.h and match the buffers secpol.msc's certmgr.dll builds: description at 0x20,
/// path pointer at 0x220, MD5 hash fields at 0x420-0x470 with the SHA-256 companion at 0x478-0x4BC.
/// </remarks>
internal sealed class SaferIdentification
{
    /// <summary>sizeof(SAFER_IDENTIFICATION_HEADER).</summary>
    public const int HeaderSize = 0x20;

    /// <summary>SAFER_MAX_DESCRIPTION_SIZE / SAFER_MAX_FRIENDLYNAME_SIZE in WCHARs.</summary>
    public const int MaxTextLength = 256;

    /// <summary>SAFER_MAX_HASH_SIZE in bytes.</summary>
    private const int MaxHashSize = 64;

    private const int TypeOffset = 0x00;
    private const int StructSizeOffset = 0x04;
    private const int GuidOffset = 0x08;
    private const int LastModifiedOffset = 0x18;
    private const int DescriptionOffset = 0x20;

    private const int ImageNameOffset = 0x220;
    private static readonly int PathSaferFlagsOffset = ImageNameOffset + IntPtr.Size;
    private static readonly int PathStructSize = Align(PathSaferFlagsOffset + sizeof(uint), IntPtr.Size);

    private const int FriendlyNameOffset = 0x220;
    private const int HashSizeOffset = 0x420;
    private const int ImageHashOffset = 0x424;
    private const int HashAlgorithmOffset = 0x464;
    private const int ImageSizeOffset = 0x468;
    private const int HashSaferFlagsOffset = 0x470;
    private const int HashStructSize = 0x478;
    private const int Hash2SizeOffset = 0x478;
    private const int Hash2ImageHashOffset = 0x47C;
    private const int Hash2AlgorithmOffset = 0x4BC;
    private const int Hash2StructSize = 0x4C0;

    private const int UrlZoneIdOffset = 0x20;
    private const int UrlZoneSaferFlagsOffset = 0x24;
    private const int UrlZoneStructSize = 0x28;

    /// <summary>The rule kind.</summary>
    public SaferIdentificationType Type { get; set; }

    /// <summary>The rule GUID (the CodeIdentifiers subkey name).</summary>
    public Guid Id { get; set; }

    /// <summary>The last modification time.</summary>
    public DateTimeOffset? LastModified { get; set; }

    /// <summary>The rule description (path and hash rules only).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The <c>dwSaferFlags</c> value.</summary>
    public uint SaferFlags { get; set; }

    /// <summary>The path or registry path (path rules).</summary>
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>The URL zone id (zone rules).</summary>
    public uint UrlZoneId { get; set; }

    /// <summary>The friendly file name (hash rules).</summary>
    public string FriendlyName { get; set; } = string.Empty;

    /// <summary>The primary hash (hash rules); MD5 for rules created by secpol.msc.</summary>
    public byte[] ImageHash { get; set; } = [];

    /// <summary>The primary hash ALG_ID.</summary>
    public uint HashAlgorithm { get; set; }

    /// <summary>The hashed file size in bytes.</summary>
    public long ImageSize { get; set; }

    /// <summary>The SHA-256 companion hash, when present.</summary>
    public byte[] Sha256Hash { get; set; } = [];

    /// <summary>The companion hash ALG_ID.</summary>
    public uint Sha256HashAlgorithm { get; set; }

    /// <summary>Builds a header-only buffer used to query a rule by GUID.</summary>
    public static byte[] CreateHeader(SaferIdentificationType type, Guid id, uint structSize)
    {
        byte[] header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(TypeOffset), (uint)type);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(StructSizeOffset), structSize);
        id.TryWriteBytes(header.AsSpan(GuidOffset));
        return header;
    }

    /// <summary>Parses an identification structure returned by <c>SaferGetLevelInformation</c>.</summary>
    public static SaferIdentification? Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize)
        {
            return null;
        }

        SaferIdentification identification = new()
        {
            Type = (SaferIdentificationType)BinaryPrimitives.ReadUInt32LittleEndian(buffer[TypeOffset..]),
            Id = new Guid(buffer.Slice(GuidOffset, 16)),
            LastModified = ReadFileTime(buffer[LastModifiedOffset..])
        };

        switch (identification.Type)
        {
            case SaferIdentificationType.ImageName when buffer.Length >= PathStructSize:
                identification.Description = ReadFixedString(buffer.Slice(DescriptionOffset, MaxTextLength * sizeof(char)));
                nint imageName = IntPtr.Size == 8
                    ? (nint)BinaryPrimitives.ReadInt64LittleEndian(buffer[ImageNameOffset..])
                    : BinaryPrimitives.ReadInt32LittleEndian(buffer[ImageNameOffset..]);
                identification.ImagePath = imageName == 0 ? string.Empty : Marshal.PtrToStringUni(imageName) ?? string.Empty;
                identification.SaferFlags = BinaryPrimitives.ReadUInt32LittleEndian(buffer[PathSaferFlagsOffset..]);
                return identification;

            case SaferIdentificationType.ImageHash when buffer.Length >= HashStructSize:
                identification.Description = ReadFixedString(buffer.Slice(DescriptionOffset, MaxTextLength * sizeof(char)));
                identification.FriendlyName = ReadFixedString(buffer.Slice(FriendlyNameOffset, MaxTextLength * sizeof(char)));
                identification.ImageHash = ReadHash(buffer, HashSizeOffset, ImageHashOffset);
                identification.HashAlgorithm = BinaryPrimitives.ReadUInt32LittleEndian(buffer[HashAlgorithmOffset..]);
                identification.ImageSize = BinaryPrimitives.ReadInt64LittleEndian(buffer[ImageSizeOffset..]);
                identification.SaferFlags = BinaryPrimitives.ReadUInt32LittleEndian(buffer[HashSaferFlagsOffset..]);
                if (buffer.Length >= Hash2StructSize)
                {
                    identification.Sha256Hash = ReadHash(buffer, Hash2SizeOffset, Hash2ImageHashOffset);
                    identification.Sha256HashAlgorithm = BinaryPrimitives.ReadUInt32LittleEndian(buffer[Hash2AlgorithmOffset..]);
                }

                return identification;

            case SaferIdentificationType.UrlZone when buffer.Length >= UrlZoneStructSize:
                identification.UrlZoneId = BinaryPrimitives.ReadUInt32LittleEndian(buffer[UrlZoneIdOffset..]);
                identification.SaferFlags = BinaryPrimitives.ReadUInt32LittleEndian(buffer[UrlZoneSaferFlagsOffset..]);
                return identification;

            default:
                return null;
        }
    }

    /// <summary>
    /// Serializes this identification. For path rules the caller must pin <paramref name="imagePath"/> and
    /// store its address with <see cref="SetImagePathPointer"/> before handing the buffer to SAFER.
    /// </summary>
    public byte[] Serialize(out string? imagePath)
    {
        imagePath = null;
        int size = Type switch
        {
            SaferIdentificationType.ImageName => PathStructSize,
            SaferIdentificationType.ImageHash => Hash2StructSize,
            SaferIdentificationType.UrlZone => UrlZoneStructSize,
            _ => throw new InvalidOperationException($"Unsupported SAFER identification type {Type}.")
        };

        byte[] buffer = new byte[size];
        Span<byte> span = buffer;
        BinaryPrimitives.WriteUInt32LittleEndian(span[TypeOffset..], (uint)Type);
        BinaryPrimitives.WriteUInt32LittleEndian(span[StructSizeOffset..], (uint)size);
        Id.TryWriteBytes(span[GuidOffset..]);
        BinaryPrimitives.WriteInt64LittleEndian(span[LastModifiedOffset..], (LastModified ?? DateTimeOffset.UtcNow).ToFileTime());

        switch (Type)
        {
            case SaferIdentificationType.ImageName:
                WriteFixedString(span.Slice(DescriptionOffset, MaxTextLength * sizeof(char)), Description);
                BinaryPrimitives.WriteUInt32LittleEndian(span[PathSaferFlagsOffset..], SaferFlags);
                imagePath = ImagePath;
                break;

            case SaferIdentificationType.ImageHash:
                WriteFixedString(span.Slice(DescriptionOffset, MaxTextLength * sizeof(char)), Description);
                WriteFixedString(span.Slice(FriendlyNameOffset, MaxTextLength * sizeof(char)), FriendlyName);
                WriteHash(span, HashSizeOffset, ImageHashOffset, ImageHash);
                BinaryPrimitives.WriteUInt32LittleEndian(span[HashAlgorithmOffset..], HashAlgorithm);
                BinaryPrimitives.WriteInt64LittleEndian(span[ImageSizeOffset..], ImageSize);
                BinaryPrimitives.WriteUInt32LittleEndian(span[HashSaferFlagsOffset..], SaferFlags);
                WriteHash(span, Hash2SizeOffset, Hash2ImageHashOffset, Sha256Hash);
                BinaryPrimitives.WriteUInt32LittleEndian(span[Hash2AlgorithmOffset..], Sha256HashAlgorithm);
                break;

            case SaferIdentificationType.UrlZone:
                BinaryPrimitives.WriteUInt32LittleEndian(span[UrlZoneIdOffset..], UrlZoneId);
                BinaryPrimitives.WriteUInt32LittleEndian(span[UrlZoneSaferFlagsOffset..], SaferFlags);
                break;
        }

        return buffer;
    }

    /// <summary>Turns a serialized identification into a delete request (<c>SaferIdentityDefault</c>).</summary>
    public static void MarkAsDelete(byte[] buffer)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(TypeOffset), (uint)SaferIdentificationType.Default);
    }

    /// <summary>Stores the pinned image path address into a serialized path identification.</summary>
    public static void SetImagePathPointer(byte[] buffer, nint imagePath)
    {
        if (IntPtr.Size == 8)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(ImageNameOffset), imagePath);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(ImageNameOffset), (int)imagePath);
        }
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    private static DateTimeOffset? ReadFileTime(ReadOnlySpan<byte> buffer)
    {
        long fileTime = BinaryPrimitives.ReadInt64LittleEndian(buffer);
        return fileTime <= 0 ? null : DateTimeOffset.FromFileTime(fileTime);
    }

    private static string ReadFixedString(ReadOnlySpan<byte> buffer)
    {
        string text = Encoding.Unicode.GetString(buffer);
        int terminator = text.IndexOf('\0');
        return terminator >= 0 ? text[..terminator] : text;
    }

    private static void WriteFixedString(Span<byte> destination, string? value)
    {
        destination.Clear();
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        // Leave room for the terminating null inside the fixed WCHAR array.
        string truncated = value.Length >= MaxTextLength ? value[..(MaxTextLength - 1)] : value;
        Encoding.Unicode.GetBytes(truncated, destination);
    }

    private static byte[] ReadHash(ReadOnlySpan<byte> buffer, int sizeOffset, int hashOffset)
    {
        uint hashSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer[sizeOffset..]);
        return hashSize is 0 or > MaxHashSize ? [] : buffer.Slice(hashOffset, (int)hashSize).ToArray();
    }

    private static void WriteHash(Span<byte> buffer, int sizeOffset, int hashOffset, byte[] hash)
    {
        if (hash.Length == 0)
        {
            return;
        }

        if (hash.Length > MaxHashSize)
        {
            throw new ArgumentOutOfRangeException(nameof(hash));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(buffer[sizeOffset..], (uint)hash.Length);
        hash.CopyTo(buffer[hashOffset..]);
    }
}
