using Microsoft.Extensions.Configuration;
using SmartArchive.Infrastructure;
using System.Text;

namespace SmartArchive.Tests;

public sealed class DocumentIntelligenceTests
{
    [Theory]
    [InlineData("Date de facture : 19/11/2026", 2026, 11, 19)]
    [InlineData("Invoice date: 2026-11-19", 2026, 11, 19)]
    [InlineData("Émise le 19 novembre 2026", 2026, 11, 19)]
    public async Task Analysis_extracts_document_date_from_text(string extractedText, int year, int month, int day)
    {
        var service = new DocumentIntelligenceService(new ConfigurationBuilder().Build());
        using var pdf = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\n"));

        var suggestion = await service.AnalyzeAsync("invoice.pdf", pdf, suppliedText: extractedText);

        Assert.Equal(new DateOnly(year, month, day), suggestion.DocumentDate);
    }
}
