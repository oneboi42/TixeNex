using System.Globalization;
using System.Text;
using CsvHelper;
using FluentAssertions;
using TixeNex.Worker.Models;
using TixeNex.Worker.Services;

namespace TixeNex.Worker.Tests.Exports;

public sealed class CsvExportTests
{
    [Fact]
    public async Task WriteCsv_NeutralizesFormulaCellsAndPreservesCsvText()
    {
        var row = new TicketExportRow
        {
            Id = 42,
            TicketNumber = "=SUM(1,1)",
            Title = "+cmd",
            Description = "-10+20",
            Status = "@SUM(A1:A2)",
            Priority = "Ordinary Unicode: Zażółć 😀",
            Requester = "Name, with comma",
            AssignedTo = "Quoted \"name\"\r\nSecond line",
            CreatedAtUtc = new DateTime(2026, 8, 24, 10, 30, 0, DateTimeKind.Utc)
        };

        await using var stream = new MemoryStream();
        await ExportService.WriteCsvAsync(
            stream,
            [row],
            CancellationToken.None);

        var csvText = Encoding.UTF8.GetString(stream.ToArray());
        csvText.Should().Contain("'=SUM(1,1)");
        csvText.Should().Contain("'+cmd");
        csvText.Should().Contain("'-10+20");
        csvText.Should().Contain("'@SUM(A1:A2)");

        stream.Position = 0;
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        var exported = csv.GetRecords<TicketExportRow>().Single();

        exported.TicketNumber.Should().Be("'=SUM(1,1)");
        exported.Title.Should().Be("'+cmd");
        exported.Description.Should().Be("'-10+20");
        exported.Status.Should().Be("'@SUM(A1:A2)");
        exported.Priority.Should().Be("Ordinary Unicode: Zażółć 😀");
        exported.Requester.Should().Be("Name, with comma");
        exported.AssignedTo.Should().Be("Quoted \"name\"\r\nSecond line");
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-1+1", "'-1+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("ordinary", "ordinary")]
    [InlineData("", "")]
    public void Neutralize_ProtectsOnlyFormulaLeadingValues(
        string input,
        string expected)
    {
        CsvCellNeutralizer.Neutralize(input).Should().Be(expected);
    }
}
