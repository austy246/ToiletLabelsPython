using System.Globalization;
using Azure.Data.Tables;
using ToiletLabels.Labels;

namespace ToiletLabels.Tests.Unit;

public class LabelEntityMapperTests
{
    [Fact]
    public void FromEntity_EmptyStringCoords_AreNull()
    {
        var entity = new TableEntity("label", "id-1") { ["Latitude"] = "", ["Longitude"] = "" };

        var label = LabelEntityMapper.FromEntity(entity);

        Assert.Null(label.Latitude);
        Assert.Null(label.Longitude);
    }

    [Fact]
    public void FromEntity_NonNumericStringCoords_AreNull()
    {
        var entity = new TableEntity("label", "id-1") { ["Latitude"] = "north", ["Longitude"] = "  " };

        var label = LabelEntityMapper.FromEntity(entity);

        Assert.Null(label.Latitude);
        Assert.Null(label.Longitude);
    }

    [Fact]
    public void FromEntity_NumericStringCoords_Parse()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
        try
        {
            var entity = new TableEntity("label", "id-1") { ["Latitude"] = "50.08", ["Longitude"] = "14.43" };

            var label = LabelEntityMapper.FromEntity(entity);

            Assert.Equal(50.08, label.Latitude);
            Assert.Equal(14.43, label.Longitude);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FromEntity_Int64Voters_DoubleAvg()
    {
        var entity = new TableEntity("label", "id-1") { ["NumVoters"] = 3L, ["AvgVote"] = 4.5 };

        var label = LabelEntityMapper.FromEntity(entity);

        Assert.Equal(3, label.NumVoters);
        Assert.Equal(4.5, label.AvgVote);
    }

    [Fact]
    public void FromEntity_Int32AvgAndStringVoters_Parse()
    {
        var entity = new TableEntity("label", "id-1") { ["NumVoters"] = "5", ["AvgVote"] = 2 };

        var label = LabelEntityMapper.FromEntity(entity);

        Assert.Equal(5, label.NumVoters);
        Assert.Equal(2.0, label.AvgVote);
    }

    [Fact]
    public void FromEntity_MissingOptionalProps_AreEmpty()
    {
        var entity = new TableEntity("label", "id-1") { ["Place"] = "Kavárna" };

        var label = LabelEntityMapper.FromEntity(entity);

        Assert.Equal("id-1", label.Id);
        Assert.Equal("Kavárna", label.Place);
        Assert.Equal("", label.City);
        Assert.Equal("", label.MenThumb);
        Assert.Equal("", label.Created);
        Assert.Equal(0, label.NumVoters);
        Assert.Equal(0.0, label.AvgVote);
    }

    [Fact]
    public void FromEntity_MapsImageAndThumbProperties()
    {
        var entity = new TableEntity("label", "id-1")
        {
            ["MenImageUrl"] = "a_men.jpg",
            ["WomenImageUrl"] = "a_women.jpg",
            ["MenThumbUrl"] = "a_men_thumb.webp",
            ["WomenThumbUrl"] = "a_women_thumb.webp",
        };

        var label = LabelEntityMapper.FromEntity(entity);

        Assert.Equal("a_men.jpg", label.MenImage);
        Assert.Equal("a_women.jpg", label.WomenImage);
        Assert.Equal("a_men_thumb.webp", label.MenThumb);
        Assert.Equal("a_women_thumb.webp", label.WomenThumb);
    }

    [Fact]
    public void ToEntity_New_WritesCountersAndCreated()
    {
        var label = new Label { Id = "id-1", Place = "Kavárna", Created = "2026-06-03T12:34:56.123456" };

        var entity = LabelEntityMapper.ToEntity(label, null);

        Assert.Equal("label", entity.PartitionKey);
        Assert.Equal("id-1", entity.RowKey);
        Assert.IsType<int>(entity["NumVoters"]);
        Assert.Equal(0, entity["NumVoters"]);
        Assert.IsType<double>(entity["AvgVote"]);
        Assert.Equal(0.0, entity["AvgVote"]);
        Assert.Equal("2026-06-03T12:34:56.123456", entity["Created"]);
        Assert.Equal("Kavárna", entity["Place"]);
    }

    [Fact]
    public void ToEntity_Existing_PreservesCounterTypes()
    {
        var existing = new TableEntity("label", "id-1")
        {
            ["NumVoters"] = 7L,
            ["AvgVote"] = 3,
            ["Created"] = "2020-01-01T00:00:00.000000",
        };
        var label = new Label
        {
            Id = "id-1",
            Place = "Nové jméno",
            NumVoters = 99,
            AvgVote = 1.5,
            Created = "2030-01-01T00:00:00.000000",
        };

        var entity = LabelEntityMapper.ToEntity(label, existing);

        Assert.IsType<long>(entity["NumVoters"]);
        Assert.Equal(7L, entity["NumVoters"]);
        Assert.IsType<int>(entity["AvgVote"]);
        Assert.Equal(3, entity["AvgVote"]);
        Assert.Equal("2020-01-01T00:00:00.000000", entity["Created"]);
        Assert.Equal("Nové jméno", entity["Place"]);
    }

    [Fact]
    public void ToEntity_Existing_DoesNotMutateExistingAndKeepsUnknownProperties()
    {
        var existing = new TableEntity("label", "id-1") { ["Place"] = "Staré", ["Extra"] = "zůstane" };

        var entity = LabelEntityMapper.ToEntity(new Label { Id = "id-1", Place = "Nové" }, existing);

        Assert.Equal("Staré", existing["Place"]);
        Assert.Equal("zůstane", entity["Extra"]);
    }

    [Fact]
    public void ToEntity_NullCoords_RemovesProperties()
    {
        var existing = new TableEntity("label", "id-1") { ["Latitude"] = 50.0, ["Longitude"] = "14.4" };

        var entity = LabelEntityMapper.ToEntity(new Label { Id = "id-1" }, existing);

        Assert.False(entity.ContainsKey("Latitude"));
        Assert.False(entity.ContainsKey("Longitude"));
    }

    [Fact]
    public void ToEntity_Coords_AreWrittenAsDoubles()
    {
        var label = new Label { Id = "id-1", Latitude = 50.08, Longitude = 14.43 };

        var entity = LabelEntityMapper.ToEntity(label, null);

        Assert.Equal(50.08, entity["Latitude"]);
        Assert.Equal(14.43, entity["Longitude"]);
    }

    [Fact]
    public void Title_FallsBackToCityThenUntitled()
    {
        Assert.Equal("Kavárna", new Label { Id = "1", Place = "Kavárna", City = "Praha" }.Title);
        Assert.Equal("Praha", new Label { Id = "1", City = "Praha" }.Title);
        Assert.Equal("Untitled", new Label { Id = "1" }.Title);
    }
}
