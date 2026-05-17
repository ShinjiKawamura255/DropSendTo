using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ShortcutSequenceMatcherTests
{
    private const ushort VkA = 0x41;
    private const ushort VkB = 0x42;
    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;

    [Fact]
    public void EvaluateKey_ShouldCompleteSingleChordMatch()
    {
        var sequence = Sequence("A", Chord(VkA, "A"));

        var result = ShortcutSequenceMatcher.EvaluateKey(
            new[] { sequence },
            Array.Empty<ShortcutSequenceProgress>(),
            captureInProgress: false,
            VkA,
            new HashSet<ushort>(),
            Array.Empty<ushort>());

        result.Result.Should().Be(ShortcutSequenceEvaluationResult.CompletedMatch);
        result.MatchedSequence.Should().BeSameAs(sequence);
        result.NextCandidates.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateKey_ShouldReturnPartialThenCompleteMultiChordMatch()
    {
        var sequence = Sequence("A B", Chord(VkA, "A"), Chord(VkB, "B"));

        var first = ShortcutSequenceMatcher.EvaluateKey(
            new[] { sequence },
            Array.Empty<ShortcutSequenceProgress>(),
            captureInProgress: false,
            VkA,
            new HashSet<ushort>(),
            Array.Empty<ushort>());

        first.Result.Should().Be(ShortcutSequenceEvaluationResult.PartialMatch);
        first.NextCandidates.Should().ContainSingle()
            .Which.NextIndex.Should().Be(1);

        var second = ShortcutSequenceMatcher.EvaluateKey(
            new[] { sequence },
            first.NextCandidates,
            captureInProgress: true,
            VkB,
            new HashSet<ushort>(),
            Array.Empty<ushort>());

        second.Result.Should().Be(ShortcutSequenceEvaluationResult.CompletedMatch);
        second.MatchedSequence.Should().BeSameAs(sequence);
        second.NextCandidates.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateKey_ShouldClearCandidates_WhenNoActiveCandidateMatches()
    {
        var sequence = Sequence("A B", Chord(VkA, "A"), Chord(VkB, "B"));
        var active = new[] { new ShortcutSequenceProgress(sequence, 1) };

        var result = ShortcutSequenceMatcher.EvaluateKey(
            new[] { sequence },
            active,
            captureInProgress: true,
            VkA,
            new HashSet<ushort>(),
            Array.Empty<ushort>());

        result.Result.Should().Be(ShortcutSequenceEvaluationResult.None);
        result.MatchedSequence.Should().BeNull();
        result.NextCandidates.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateKey_ShouldPreferCompleteMatch_WhenPartialAlsoMatches()
    {
        var single = Sequence("A", Chord(VkA, "A"));
        var multi = Sequence("A B", Chord(VkA, "A"), Chord(VkB, "B"));

        var result = ShortcutSequenceMatcher.EvaluateKey(
            new[] { single, multi },
            Array.Empty<ShortcutSequenceProgress>(),
            captureInProgress: false,
            VkA,
            new HashSet<ushort>(),
            Array.Empty<ushort>());

        result.Result.Should().Be(ShortcutSequenceEvaluationResult.CompletedMatch);
        result.MatchedSequence.Should().BeSameAs(single);
        result.NextCandidates.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateKey_ShouldUsePrefixResidue_ForFirstChordSharedModifier()
    {
        var sequence = Sequence("Ctrl+A", Chord(VkA, "A", ModifierKind.Control));

        var result = ShortcutSequenceMatcher.EvaluateKey(
            new[] { sequence },
            Array.Empty<ShortcutSequenceProgress>(),
            captureInProgress: false,
            VkA,
            new HashSet<ushort>(),
            new[] { VkControl });

        result.Result.Should().Be(ShortcutSequenceEvaluationResult.CompletedMatch);
        result.MatchedSequence.Should().BeSameAs(sequence);
    }

    [Fact]
    public void EvaluateKey_ShouldNotUsePrefixResidue_ForSecondChord()
    {
        var sequence = Sequence("A Ctrl+B", Chord(VkA, "A"), Chord(VkB, "B", ModifierKind.Control));
        var active = new[] { new ShortcutSequenceProgress(sequence, 1) };

        var result = ShortcutSequenceMatcher.EvaluateKey(
            new[] { sequence },
            active,
            captureInProgress: true,
            VkB,
            new HashSet<ushort>(),
            new[] { VkControl });

        result.Result.Should().Be(ShortcutSequenceEvaluationResult.None);
        result.MatchedSequence.Should().BeNull();
    }

    [Fact]
    public void EvaluateKey_ShouldRejectExtraModifiersAndResidue()
    {
        var sequence = Sequence("Ctrl+A", Chord(VkA, "A", ModifierKind.Control));

        var result = ShortcutSequenceMatcher.EvaluateKey(
            new[] { sequence },
            Array.Empty<ShortcutSequenceProgress>(),
            captureInProgress: false,
            VkA,
            new HashSet<ushort> { VkShift },
            new[] { VkControl });

        result.Result.Should().Be(ShortcutSequenceEvaluationResult.None);
    }

    private static ShortcutSequence Sequence(string text, params KeyChord[] chords) => new(chords, text);

    private static KeyChord Chord(ushort mainKey, string token, params ModifierKind[] modifiers) =>
        new(mainKey, token, modifiers, string.Join("+", modifiers.Select(m => m.ToString()).Append(token)));
}
