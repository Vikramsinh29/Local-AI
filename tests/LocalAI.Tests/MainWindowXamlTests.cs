using System.Xml.Linq;

namespace LocalAI.Tests;

public sealed class MainWindowXamlTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ProjectInstructionReadOnlyBindings_AreExplicitlyOneWay()
    {
        string xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml");
        XDocument document = XDocument.Load(xamlPath);
        string[] runBindings = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Run")
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        Assert.Contains(
            runBindings,
            binding => binding.Contains(
                    "InclusionText",
                    StringComparison.Ordinal) &&
                binding.Contains(
                    "Mode=OneWay",
                    StringComparison.Ordinal));
        Assert.Contains(
            runBindings,
            binding => binding.Contains(
                    "StateReason",
                    StringComparison.Ordinal) &&
                binding.Contains(
                    "Mode=OneWay",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ConversationAndComposer_HaveIndependentBoundedScrollRegions()
    {
        string xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml");
        XDocument document = XDocument.Load(xamlPath);

        XElement mainChatGrid = FindNamedElement(
            document,
            "MainChatGrid");
        XElement conversationScrollViewer = FindNamedElement(
            document,
            "ConversationScrollViewer");
        XElement composerHostGrid = FindNamedElement(
            document,
            "ComposerHostGrid");
        XElement composerScrollViewer = FindNamedElement(
            document,
            "ComposerScrollViewer");

        XElement[] rowDefinitions = mainChatGrid
            .Elements()
            .Single(element =>
                element.Name.LocalName == "Grid.RowDefinitions")
            .Elements()
            .ToArray();

        Assert.Equal(3, rowDefinitions.Length);
        Assert.Equal("2*", rowDefinitions[1].Attribute("Height")?.Value);
        Assert.Equal("160", rowDefinitions[1].Attribute("MinHeight")?.Value);
        Assert.Equal("3*", rowDefinitions[2].Attribute("Height")?.Value);
        Assert.Equal("280", rowDefinitions[2].Attribute("MinHeight")?.Value);

        Assert.Equal(
            "1",
            conversationScrollViewer.Attribute("Grid.Row")?.Value);
        Assert.Equal(
            "Auto",
            conversationScrollViewer
                .Attribute("VerticalScrollBarVisibility")?.Value);

        Assert.Equal(
            "2",
            composerHostGrid.Attribute("Grid.Row")?.Value);
        Assert.Equal(
            "Auto",
            composerScrollViewer
                .Attribute("VerticalScrollBarVisibility")?.Value);
    }

    private static XElement FindNamedElement(
        XDocument document,
        string name)
    {
        return document
            .Descendants()
            .Single(element =>
                element.Attribute(XamlNamespace + "Name")?.Value == name);
    }
}
