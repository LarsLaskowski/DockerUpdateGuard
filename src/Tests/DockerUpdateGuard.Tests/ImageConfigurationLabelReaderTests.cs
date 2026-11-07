using System.Text.Json;

using DockerUpdateGuard.Images.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ImageConfigurationLabelReader"/>
/// </summary>
[TestClass]
public class ImageConfigurationLabelReaderTests
{
    #region Methods

    /// <summary>
    /// Verify names that differ only in case give one entry with the name and the value of the last one
    /// </summary>
    [TestMethod]
    [TestCategory("AC1")]
    public void ImageConfigurationLabelReaderReadLabelsWhenNamesDifferOnlyInCaseKeepsTheLast()
    {
        using var document = JsonDocument.Parse("""
                                                {
                                                  "org.opencontainers.image.base.name": "first",
                                                  "other": "kept",
                                                  "Org.Opencontainers.Image.Base.Name": "last"
                                                }
                                                """);

        var labels = ImageConfigurationLabelReader.ReadLabels(document.RootElement);

        Assert.HasCount(2, labels, "The colliding names must give one entry and the other label must be kept");
        Assert.AreEqual("last", labels["org.opencontainers.image.base.name"], "A lookup by the lowercase name must find the last value");
        Assert.AreEqual("last", labels["ORG.OPENCONTAINERS.IMAGE.BASE.NAME"], "A lookup by any case must find the last value");
        Assert.AreEqual("kept", labels["other"], "The other label must be kept");
        Assert.Contains("Org.Opencontainers.Image.Base.Name", labels.Keys.ToArray(), "The entry must carry the name of the last label");
        Assert.DoesNotContain("org.opencontainers.image.base.name", labels.Keys.ToArray(), "The name of the earlier label must not be kept");
    }

    /// <summary>
    /// Verify a name that repeats exactly keeps the value of the last occurrence
    /// </summary>
    [TestMethod]
    [TestCategory("AC2")]
    public void ImageConfigurationLabelReaderReadLabelsWhenNameRepeatsKeepsTheLast()
    {
        using var document = JsonDocument.Parse("""{ "a": "1", "a": "3" }""");

        var labels = ImageConfigurationLabelReader.ReadLabels(document.RootElement);

        var label = Assert.ContainsSingle(labels, "A repeated name must give one entry");

        Assert.AreEqual("a", label.Key, "The entry must carry the repeated name");
        Assert.AreEqual("3", label.Value, "The value of the last occurrence must be kept");
    }

    /// <summary>
    /// Verify a later label whose value is not a string does not replace an earlier string label of a colliding name
    /// </summary>
    [TestMethod]
    [TestCategory("AC3")]
    public void ImageConfigurationLabelReaderReadLabelsWhenLaterValueIsNotStringKeepsTheEarlier()
    {
        using var document = JsonDocument.Parse("""{ "Name": "text", "name": 5, "NAME": null, "other": { "x": 1 } }""");

        var labels = ImageConfigurationLabelReader.ReadLabels(document.RootElement);

        var label = Assert.ContainsSingle(labels, "Only the string label must be kept");

        Assert.AreEqual("Name", label.Key, "The earlier string label must keep its name");
        Assert.AreEqual("text", label.Value, "The earlier string value must not be replaced by a non-string one");
    }

    /// <summary>
    /// Verify control characters in a value are replaced and a null value gives an empty string
    /// </summary>
    [TestMethod]
    [TestCategory("AC3")]
    public void ImageConfigurationLabelReaderReadLabelsWhenValueHasControlCharactersReplacesThem()
    {
        var json = """{ "a": "1\u000A.0\u001B", "plain": "unchanged" }""";

        Assert.Contains("\\u000A", json, "The test input must carry the escaped line feed");

        using var document = JsonDocument.Parse(json);

        var labels = ImageConfigurationLabelReader.ReadLabels(document.RootElement);

        Assert.AreEqual("1 .0 ", labels["a"], "The control characters of a value must be replaced");
        Assert.AreEqual("unchanged", labels["plain"], "A value without such a character must stay unchanged");
    }

    /// <summary>
    /// Verify a label whose value is null gives no entry, so no empty string is produced for it
    /// </summary>
    [TestMethod]
    [TestCategory("AC3")]
    public void ImageConfigurationLabelReaderReadLabelsWhenValueIsNullGivesNoEntry()
    {
        using var document = JsonDocument.Parse("""{ "nothing": null, "empty": "" }""");

        var labels = ImageConfigurationLabelReader.ReadLabels(document.RootElement);

        var label = Assert.ContainsSingle(labels, "Only the string label must give an entry");

        Assert.AreEqual("empty", label.Key, "The empty string label must be kept");
        Assert.AreEqual(string.Empty, label.Value, "The empty string value must stay empty");
        Assert.DoesNotContain("nothing", labels.Keys.ToArray(), "A null value must give no entry");
    }

    /// <summary>
    /// Verify a labels element that is not an object gives an empty result
    /// </summary>
    /// <param name="json">Json text of the labels element</param>
    [TestMethod]
    [DataRow("null")]
    [DataRow("[\"a\", \"b\"]")]
    [DataRow("\"text\"")]
    [DataRow("5")]
    [TestCategory("AC3")]
    public void ImageConfigurationLabelReaderReadLabelsWhenElementIsNotObjectGivesEmpty(string json)
    {
        using var document = JsonDocument.Parse(json);

        var labels = ImageConfigurationLabelReader.ReadLabels(document.RootElement);

        Assert.IsEmpty(labels, "A labels element that is not an object must give no labels");
    }

    /// <summary>
    /// Verify an undefined labels element gives an empty result
    /// </summary>
    [TestMethod]
    [TestCategory("AC3")]
    public void ImageConfigurationLabelReaderReadLabelsWhenElementIsUndefinedGivesEmpty()
    {
        var labels = ImageConfigurationLabelReader.ReadLabels(default);

        Assert.IsEmpty(labels, "An undefined labels element must give no labels");
    }

    #endregion // Methods
}