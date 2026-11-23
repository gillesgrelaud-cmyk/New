namespace TransfertPhotos.Services;

public sealed record PhoneInfo(string Name);

public sealed record TransferOptions(bool OrganizeByDate, bool SkipDuplicates);

public sealed record ScanResult(int PhotoCount, int VideoCount, long TotalBytes);

public sealed record TransferResult(int Copied, int Skipped, long Bytes);
