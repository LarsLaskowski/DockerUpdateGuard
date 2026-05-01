using System.Text.RegularExpressions;

using DockerUpdateGuard.Images.Data;

namespace DockerUpdateGuard.Images.Helper;

/// <summary>
/// Helper for resolving semantic version tags behind alias tags
/// </summary>
public static class VersionTagResolutionHelper
{
    #region Const fields

    /// <summary>
    /// Strict numeric version-tag pattern
    /// </summary>
    private static readonly Regex _numericVersionTagExpression = new("^[vV]?(?<major>\\d+)\\.(?<minor>\\d+)\\.(?<patch>\\d+)(?<suffix>-.+)?$",
                                                                     RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Numeric version-line tag pattern
    /// </summary>
    private static readonly Regex _numericVersionLineTagExpression = new("^[vV]?(?<major>\\d+)\\.(?<minor>\\d+)(?<suffix>-.+)?$",
                                                                         RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Year-prefixed version-tag pattern
    /// </summary>
    private static readonly Regex _yearPrefixedTagExpression = new("^(?<year>\\d{4})-(?<suffix>.+)$",
                                                                   RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    #endregion // Const fields

    #region Methods

    /// <summary>
    /// Resolve the semantic version tag behind an alias tag with the same digest
    /// </summary>
    /// <param name="currentTag">Current tag</param>
    /// <param name="currentDigest">Current digest</param>
    /// <param name="candidates">Available candidates</param>
    /// <returns>Resolved semantic version tag or null</returns>
    public static string? ResolveAliasVersionTag(string currentTag,
                                                 string? currentDigest,
                                                 IEnumerable<VersionTagCandidateData> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentTag);
        ArgumentNullException.ThrowIfNull(candidates);

        if (TryParseVersionTag(currentTag, out _))
        {
            return null;
        }

        var candidateList = candidates.Where(entity => string.IsNullOrWhiteSpace(entity.Tag) == false)
                                      .ToList();
        var currentTagCandidate = candidateList.FirstOrDefault(entity => string.Equals(entity.Tag,
                                                                                       currentTag,
                                                                                       StringComparison.OrdinalIgnoreCase)
                                                                         && string.Equals(entity.Digest ?? string.Empty,
                                                                                          currentDigest ?? string.Empty,
                                                                                          StringComparison.OrdinalIgnoreCase))
                                      ?? candidateList.FirstOrDefault(entity => string.Equals(entity.Tag,
                                                                                              currentTag,
                                                                                              StringComparison.OrdinalIgnoreCase));
        var digest = string.IsNullOrWhiteSpace(currentDigest) == false
                         ? currentDigest
                         : currentTagCandidate?.Digest;
        var restrictToVersionLine = TryParseVersionLineTag(currentTag,
                                                           out _,
                                                           out _,
                                                           out _);

        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        return candidateList.Where(entity => string.Equals(entity.Digest,
                                                           digest,
                                                           StringComparison.OrdinalIgnoreCase)
                                             && string.Equals(entity.Tag,
                                                              currentTag,
                                                              StringComparison.OrdinalIgnoreCase) == false
                                             && TryParseVersionTag(entity.Tag,
                                                                   out _)
                                             && (restrictToVersionLine == false
                                                 || IsMatchingVersionLineTag(currentTag, entity.Tag)))
                            .Select(entity => new
                                              {
                                                  Candidate = entity,
                                                  Version = ParseVersionTag(entity.Tag),
                                              })
                            .OrderByDescending(entity => entity.Candidate.PublishedAtUtc)
                            .ThenByDescending(entity => entity.Version)
                            .Select(entity => entity.Candidate.Tag)
                            .FirstOrDefault();
    }

    /// <summary>
    /// Resolve a version tag suitable for UI display
    /// </summary>
    /// <param name="tag">Tag to display</param>
    /// <param name="digest">Digest to display</param>
    /// <param name="candidates">Available candidates</param>
    /// <returns>Displayable semantic version tag or null</returns>
    public static string? ResolveDisplayVersionTag(string tag,
                                                   string? digest,
                                                   IEnumerable<VersionTagCandidateData> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentNullException.ThrowIfNull(candidates);

        return IsDisplayableSpecificVersionTag(tag)
                   ? tag
                   : ResolveAliasVersionTag(tag, digest, candidates);
    }

    /// <summary>
    /// Determine whether a tag can be displayed as a concrete version tag
    /// </summary>
    /// <param name="value">Candidate tag value</param>
    /// <returns>True when the tag is a supported concrete version tag</returns>
    public static bool IsDisplayableSpecificVersionTag(string value)
    {
        return TryParseVersionTag(value, out _)
               || TryParseYearPrefixedTag(value, out _, out _);
    }

    /// <summary>
    /// Determine whether a concrete version tag belongs to the same version line
    /// </summary>
    /// <param name="tag">Current exact or channel tag</param>
    /// <param name="candidateTag">Candidate concrete tag</param>
    /// <returns>True when the candidate belongs to the same version line and variant family</returns>
    public static bool IsMatchingVersionLineTag(string tag, string candidateTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateTag);

        if (TryParseVersionTagComponents(tag, out var version, out var variantFamilyKey))
        {
            return TryParseVersionTagComponents(candidateTag, out var candidateVersion, out var candidateVariantFamilyKey)
                   && candidateVersion.Major == version.Major
                   && candidateVersion.Minor == version.Minor
                   && string.Equals(candidateVariantFamilyKey,
                                    variantFamilyKey,
                                    StringComparison.OrdinalIgnoreCase);
        }

        return TryParseVersionLineTag(tag, out var major, out var minor, out variantFamilyKey)
               && TryParseVersionTagComponents(candidateTag, out var lineCandidateVersion, out var lineCandidateVariantFamilyKey)
               && lineCandidateVersion.Major == major
               && lineCandidateVersion.Minor == minor
               && string.Equals(lineCandidateVariantFamilyKey,
                                variantFamilyKey,
                                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compare two concrete version tags from the same variant family
    /// </summary>
    /// <param name="left">Left concrete tag</param>
    /// <param name="right">Right concrete tag</param>
    /// <param name="comparison">Comparison result</param>
    /// <returns>True when both tags are concrete versions from the same family</returns>
    public static bool TryCompareVersionTags(string left, string right, out int comparison)
    {
        comparison = 0;

        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentException.ThrowIfNullOrWhiteSpace(right);

        if (TryParseVersionTagComponents(left, out var leftVersion, out var leftVariantFamilyKey) == false
            || TryParseVersionTagComponents(right, out var rightVersion, out var rightVariantFamilyKey) == false
            || string.Equals(leftVariantFamilyKey,
                             rightVariantFamilyKey,
                             StringComparison.OrdinalIgnoreCase) == false)
        {
            return false;
        }

        comparison = leftVersion.CompareTo(rightVersion);

        return true;
    }

    /// <summary>
    /// Compare two supported display-version tags
    /// </summary>
    /// <param name="left">Left display tag</param>
    /// <param name="right">Right display tag</param>
    /// <param name="comparison">Comparison result</param>
    /// <returns>True when both tags can be compared</returns>
    public static bool TryCompareDisplayVersionTags(string left, string right, out int comparison)
    {
        comparison = 0;

        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentException.ThrowIfNullOrWhiteSpace(right);

        if (TryCompareVersionTags(left, right, out comparison))
        {
            return true;
        }

        if (TryParseYearPrefixedTag(left, out _, out _)
            && TryParseYearPrefixedTag(right, out _, out _))
        {
            comparison = CompareYearPrefixedTags(left, right);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempt to parse a semantic version tag
    /// </summary>
    /// <param name="value">Candidate tag value</param>
    /// <param name="version">Parsed version</param>
    /// <returns>True when parsing succeeded</returns>
    public static bool TryParseVersionTag(string value, out Version version)
    {
        return TryParseVersionTagComponents(value, out version, out _);
    }

    /// <summary>
    /// Parse a semantic version tag
    /// </summary>
    /// <param name="value">Candidate tag value</param>
    /// <returns>Parsed version</returns>
    public static Version ParseVersionTag(string value)
    {
        return TryParseVersionTag(value, out var version)
                   ? version
                   : throw new FormatException($"Tag '{value}' is not a supported numeric version tag");
    }

    /// <summary>
    /// Attempt to parse a year-prefixed tag
    /// </summary>
    /// <param name="value">Candidate tag value</param>
    /// <param name="year">Parsed major year</param>
    /// <param name="suffix">Parsed suffix after the leading year</param>
    /// <returns>True when parsing succeeded</returns>
    public static bool TryParseYearPrefixedTag(string value,
                                               out int year,
                                               out string suffix)
    {
        year = 0;
        suffix = string.Empty;

        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var match = _yearPrefixedTagExpression.Match(value.Trim());

        if (match.Success == false)
        {
            return false;
        }

        year = int.Parse(match.Groups["year"].Value);
        suffix = match.Groups["suffix"].Value;

        return true;
    }

    /// <summary>
    /// Compare two year-prefixed tags
    /// </summary>
    /// <param name="left">Left tag</param>
    /// <param name="right">Right tag</param>
    /// <returns>Comparison result</returns>
    public static int CompareYearPrefixedTags(string left, string right)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentException.ThrowIfNullOrWhiteSpace(right);

        if (TryParseYearPrefixedTag(left, out var leftYear, out var leftSuffix) == false
            || TryParseYearPrefixedTag(right, out var rightYear, out var rightSuffix) == false)
        {
            throw new FormatException("Both values must be year-prefixed tags");
        }

        var yearComparison = leftYear.CompareTo(rightYear);

        return yearComparison != 0
                   ? yearComparison
                   : CompareNaturalSortStrings(leftSuffix, rightSuffix);
    }

    /// <summary>
    /// Attempt to parse a numeric version-line tag
    /// </summary>
    /// <param name="value">Candidate tag value</param>
    /// <param name="major">Parsed major value</param>
    /// <param name="minor">Parsed minor value</param>
    /// <param name="variantFamilyKey">Normalized variant-family key</param>
    /// <returns>True when parsing succeeded</returns>
    private static bool TryParseVersionLineTag(string value,
                                               out int major,
                                               out int minor,
                                               out string variantFamilyKey)
    {
        major = 0;
        minor = 0;
        variantFamilyKey = string.Empty;

        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var match = _numericVersionLineTagExpression.Match(value.Trim());

        if (match.Success == false)
        {
            return false;
        }

        major = int.Parse(match.Groups["major"].Value);
        minor = int.Parse(match.Groups["minor"].Value);
        variantFamilyKey = NormalizeVariantFamilyKey(match.Groups["suffix"].Value);

        return true;
    }

    /// <summary>
    /// Attempt to parse a concrete version tag together with its variant-family key
    /// </summary>
    /// <param name="value">Candidate tag value</param>
    /// <param name="version">Parsed version</param>
    /// <param name="variantFamilyKey">Normalized variant-family key</param>
    /// <returns>True when parsing succeeded</returns>
    private static bool TryParseVersionTagComponents(string value,
                                                     out Version version,
                                                     out string variantFamilyKey)
    {
        version = new Version();
        variantFamilyKey = string.Empty;

        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var match = _numericVersionTagExpression.Match(value.Trim());

        if (match.Success == false)
        {
            return false;
        }

        version = new Version(int.Parse(match.Groups["major"].Value),
                              int.Parse(match.Groups["minor"].Value),
                              int.Parse(match.Groups["patch"].Value));
        variantFamilyKey = NormalizeVariantFamilyKey(match.Groups["suffix"].Value);

        return true;
    }

    /// <summary>
    /// Normalize a variant suffix into a comparable family key
    /// </summary>
    /// <param name="suffix">Raw variant suffix</param>
    /// <returns>Normalized family key</returns>
    private static string NormalizeVariantFamilyKey(string? suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return string.Empty;
        }

        var trimmedSuffix = suffix.Trim().TrimStart('-');

        if (string.IsNullOrWhiteSpace(trimmedSuffix))
        {
            return string.Empty;
        }

        return string.Join('-',
                           trimmedSuffix.Split('-', StringSplitOptions.RemoveEmptyEntries)
                                        .Select(NormalizeVariantSegment)
                                        .Where(entity => string.IsNullOrWhiteSpace(entity) == false));
    }

    /// <summary>
    /// Normalize a single variant segment
    /// </summary>
    /// <param name="segment">Raw variant segment</param>
    /// <returns>Normalized segment</returns>
    private static string NormalizeVariantSegment(string segment)
    {
        var trimmedSegment = segment.Trim().ToLowerInvariant();
        var letterCount = 0;

        while (letterCount < trimmedSegment.Length && char.IsLetter(trimmedSegment[letterCount]))
        {
            letterCount++;
        }

        return letterCount == 0
                   ? trimmedSegment
                   : trimmedSegment[..letterCount];
    }

    /// <summary>
    /// Compare strings using a simple natural sort order
    /// </summary>
    /// <param name="left">Left value</param>
    /// <param name="right">Right value</param>
    /// <returns>Comparison result</returns>
    private static int CompareNaturalSortStrings(string left, string right)
    {
        var leftIndex = 0;
        var rightIndex = 0;

        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
            {
                var leftNumberStart = leftIndex;
                var rightNumberStart = rightIndex;

                while (leftIndex < left.Length && char.IsDigit(left[leftIndex]))
                {
                    leftIndex++;
                }

                while (rightIndex < right.Length && char.IsDigit(right[rightIndex]))
                {
                    rightIndex++;
                }

                var leftNumber = left[leftNumberStart..leftIndex].TrimStart('0');
                var rightNumber = right[rightNumberStart..rightIndex].TrimStart('0');

                leftNumber = string.IsNullOrWhiteSpace(leftNumber) ? "0" : leftNumber;
                rightNumber = string.IsNullOrWhiteSpace(rightNumber) ? "0" : rightNumber;

                var lengthComparison = leftNumber.Length.CompareTo(rightNumber.Length);

                if (lengthComparison != 0)
                {
                    return lengthComparison;
                }

                var numberComparison = string.Compare(leftNumber, rightNumber, StringComparison.Ordinal);

                if (numberComparison != 0)
                {
                    return numberComparison;
                }

                continue;
            }

            var leftTextStart = leftIndex;
            var rightTextStart = rightIndex;

            while (leftIndex < left.Length && char.IsDigit(left[leftIndex]) == false)
            {
                leftIndex++;
            }

            while (rightIndex < right.Length && char.IsDigit(right[rightIndex]) == false)
            {
                rightIndex++;
            }

            var textComparison = string.Compare(left[leftTextStart..leftIndex],
                                                right[rightTextStart..rightIndex],
                                                StringComparison.OrdinalIgnoreCase);

            if (textComparison != 0)
            {
                return textComparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    #endregion // Methods
}