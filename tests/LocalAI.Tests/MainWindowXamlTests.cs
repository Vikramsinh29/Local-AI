using System.Xml.Linq;

namespace LocalAI.Tests;

public sealed class MainWindowXamlTests
{
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
}
