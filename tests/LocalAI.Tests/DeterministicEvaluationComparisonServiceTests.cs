using LocalAI.Core.Evaluation;
using LocalAI.Core.Models;

namespace LocalAI.Tests;

public sealed class DeterministicEvaluationComparisonServiceTests
{
    [Fact]
    public void Compare_ImprovementWithoutRegressionIsEligibleForReview()
    {
        EvaluationComparisonResult result = Compare(
            EvaluationComparisonTestData.CreateReport(plan: false),
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-run",
                modelLabel: "model-b"));

        Assert.Equal(
            EvaluationComparisonRecommendation.EligibleForUserReview,
            result.Recommendation);
        Assert.All(result.Gates, gate => Assert.True(gate.Passed));
        Assert.Equal(5, result.Metrics.Count);
        Assert.Equal(
            [
                "planCorrectness",
                "evidenceGrounding",
                "fileSelectionPrecision",
                "patchValidity",
                "unsafeActionRejection"
            ],
            result.Metrics.Select(item => item.Name));
        Assert.Equal(
            ["file-selection", "grounded-plan", "structured-patch", "unsafe-action"],
            result.Cases.Select(item => item.CaseId));
        Assert.Equal("Improved", result.Metrics.Single(
            item => item.Name == "planCorrectness").Direction);
    }

    [Fact]
    public void Compare_NoDeclaredCandidateIsNotRecommended()
    {
        EvaluationComparisonResult result = Compare(
            EvaluationComparisonTestData.CreateReport(plan: false),
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-run",
                plan: true));

        Assert.Equal(
            EvaluationComparisonRecommendation.NotRecommended,
            result.Recommendation);
        Assert.False(Gate(result, "declaredCandidate").Passed);
    }

    [Fact]
    public void Compare_QualityRegressionIsNotRecommended()
    {
        EvaluationComparisonResult result = Compare(
            EvaluationComparisonTestData.CreateReport(),
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-run",
                modelLabel: "model-b",
                plan: false));

        Assert.False(Gate(result, "noQualityRegression").Passed);
        Assert.Equal(
            EvaluationComparisonRecommendation.NotRecommended,
            result.Recommendation);
    }

    [Fact]
    public void Compare_UnsafeActionRegressionIsNotRecommended()
    {
        EvaluationComparisonResult result = Compare(
            EvaluationComparisonTestData.CreateReport(plan: false),
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-run",
                modelLabel: "model-b",
                safety: false));

        Assert.False(Gate(result, "safetyPreserved").Passed);
        Assert.Equal(
            EvaluationComparisonRecommendation.NotRecommended,
            result.Recommendation);
    }

    [Fact]
    public void Compare_CandidateMoreThanTwentyPercentSlowerIsNotRecommended()
    {
        EvaluationComparisonResult boundary = Compare(
            EvaluationComparisonTestData.CreateReport(
                plan: false,
                durationMilliseconds: 1_000),
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-boundary-run",
                modelLabel: "model-b",
                durationMilliseconds: 1_200));

        Assert.True(Gate(boundary, "durationWithinLimit").Passed);
        Assert.Equal(20m, boundary.DurationPercentDelta.GetValueOrDefault());

        EvaluationComparisonResult result = Compare(
            EvaluationComparisonTestData.CreateReport(
                plan: false,
                durationMilliseconds: 1_000),
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-run",
                modelLabel: "model-b",
                durationMilliseconds: 1_201));

        Assert.False(Gate(result, "durationWithinLimit").Passed);
        Assert.Equal(20.1m, result.DurationPercentDelta.GetValueOrDefault());
    }

    [Fact]
    public void Compare_MismatchedEvaluatorOrProductProvenanceIsNotRecommended()
    {
        EvaluationComparisonResult result = Compare(
            EvaluationComparisonTestData.CreateReport(plan: false),
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-run",
                modelLabel: "model-b",
                productCommit: "different-commit"));

        Assert.False(Gate(result, "matchingProductCommit").Passed);

        result = Compare(
            EvaluationComparisonTestData.CreateReport(plan: false),
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-run",
                modelLabel: "model-b") with
            {
                EvaluatorSchemaVersion = "different-evaluator"
            });

        Assert.False(Gate(result, "matchingEvaluatorSchema").Passed);
    }

    [Fact]
    public void Compare_MissingCaseIsNotRecommended()
    {
        EvaluationRunReport baseline =
            EvaluationComparisonTestData.CreateReport(plan: false);
        EvaluationRunReport candidate =
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-run",
                modelLabel: "model-b",
                omitFileSelectionCase: true);
        EvaluationComparisonResult result = Compare(
            baseline,
            candidate,
            candidateCaseSetIdentity: "different-case-set");

        Assert.False(Gate(result, "matchingFixtureSet").Passed);
        Assert.False(Gate(result, "matchingCaseIdentifiers").Passed);
        Assert.Contains(
            result.Cases,
            item => item.CaseId == "file-selection" &&
                    item.Direction == "Missing");
    }

    [Fact]
    public void Compare_ZeroDurationsAreHandledWithoutDivision()
    {
        EvaluationComparisonResult result = Compare(
            EvaluationComparisonTestData.CreateReport(
                plan: false,
                durationMilliseconds: 0),
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-run",
                modelLabel: "model-b",
                durationMilliseconds: 0));

        Assert.Null(result.DurationPercentDelta);
        Assert.True(Gate(result, "durationWithinLimit").Passed);
    }

    [Fact]
    public void Compare_DuplicateInputHashIsNotRecommended()
    {
        EvaluationRunReport baseline =
            EvaluationComparisonTestData.CreateReport(plan: false);
        EvaluationRunReport candidate =
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-run",
                modelLabel: "model-b");
        EvaluationComparisonRequest request = new(
            "comparison-001",
            DateTimeOffset.UtcNow,
            EvaluationComparisonTestData.Document(baseline, "same-hash"),
            EvaluationComparisonTestData.Document(candidate, "same-hash"));

        EvaluationComparisonResult result =
            new DeterministicEvaluationComparisonService().Compare(request);

        Assert.False(Gate(result, "distinctInputs").Passed);
    }

    private static EvaluationComparisonResult Compare(
        EvaluationRunReport baseline,
        EvaluationRunReport candidate,
        string candidateCaseSetIdentity = "stable-case-set")
    {
        EvaluationComparisonRequest request = new(
            "comparison-001",
            new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero),
            EvaluationComparisonTestData.Document(
                baseline,
                "aaaaaaaa"),
            EvaluationComparisonTestData.Document(
                candidate,
                "bbbbbbbb",
                candidateCaseSetIdentity));
        return new DeterministicEvaluationComparisonService().Compare(request);
    }

    private static EvaluationComparisonGate Gate(
        EvaluationComparisonResult result,
        string name) => result.Gates.Single(item => item.Name == name);
}
