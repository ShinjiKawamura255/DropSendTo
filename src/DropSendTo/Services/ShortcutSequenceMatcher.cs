using System.Collections.Generic;

namespace DropSendTo.Services;

internal static class ShortcutSequenceMatcher
{
    public static ShortcutSequenceEvaluation EvaluateKey(
        IReadOnlyList<ShortcutSequence> availableSequences,
        IReadOnlyList<ShortcutSequenceProgress> activeCandidates,
        bool captureInProgress,
        ushort mainKey,
        HashSet<ushort> modifiers,
        IReadOnlyCollection<ushort> prefixResidue)
    {
        if (availableSequences.Count == 0)
        {
            return ShortcutSequenceEvaluation.None;
        }

        var nextCandidates = new List<ShortcutSequenceProgress>();
        if (!captureInProgress)
        {
            foreach (var sequence in availableSequences)
            {
                if (sequence.Chords.Count == 0)
                {
                    continue;
                }

                var chord = sequence.Chords[0];
                if (!Matches(chord, mainKey, modifiers, prefixResidue))
                {
                    continue;
                }

                if (sequence.Chords.Count == 1)
                {
                    return ShortcutSequenceEvaluation.Completed(sequence);
                }

                nextCandidates.Add(new ShortcutSequenceProgress(sequence, 1));
            }
        }
        else
        {
            foreach (var candidate in activeCandidates)
            {
                if (candidate.NextIndex >= candidate.Sequence.Chords.Count)
                {
                    continue;
                }

                var chord = candidate.Sequence.Chords[candidate.NextIndex];
                if (!Matches(chord, mainKey, modifiers, System.Array.Empty<ushort>()))
                {
                    continue;
                }

                if (candidate.NextIndex + 1 == candidate.Sequence.Chords.Count)
                {
                    return ShortcutSequenceEvaluation.Completed(candidate.Sequence);
                }

                nextCandidates.Add(new ShortcutSequenceProgress(candidate.Sequence, candidate.NextIndex + 1));
            }
        }

        if (nextCandidates.Count > 0)
        {
            return ShortcutSequenceEvaluation.Partial(nextCandidates);
        }

        return ShortcutSequenceEvaluation.None;
    }

    private static bool Matches(
        KeyChord chord,
        ushort mainKey,
        HashSet<ushort> modifiers,
        IReadOnlyCollection<ushort> prefixResidue)
    {
        if (chord.MainKey != mainKey) return false;
        var working = new HashSet<ushort>(modifiers);
        var prefixWorking = prefixResidue.Count > 0 ? new List<ushort>(prefixResidue) : null;
        foreach (var modifier in chord.Modifiers)
        {
            if (TryConsumeModifier(working, modifier))
            {
                continue;
            }

            if (prefixWorking is null || !TryConsumeModifier(prefixWorking, modifier))
            {
                return false;
            }
        }
        if (working.Count > 0)
        {
            return false;
        }

        if (prefixWorking is null || prefixWorking.Count == 0)
        {
            return true;
        }

        return chord.Modifiers.Count > 0;
    }

    private static bool TryConsumeModifier(ICollection<ushort> actual, ModifierKind modifier)
    {
        foreach (var candidate in KeyChordParser.GetCandidateModifierVirtualKeys(modifier))
        {
            if (actual.Remove(candidate))
            {
                return true;
            }
        }
        return false;
    }
}

internal enum ShortcutSequenceEvaluationResult
{
    None,
    PartialMatch,
    CompletedMatch
}

internal readonly struct ShortcutSequenceProgress
{
    public ShortcutSequenceProgress(ShortcutSequence sequence, int nextIndex)
    {
        Sequence = sequence;
        NextIndex = nextIndex;
    }

    public ShortcutSequence Sequence { get; }
    public int NextIndex { get; }
}

internal readonly struct ShortcutSequenceEvaluation
{
    private ShortcutSequenceEvaluation(
        ShortcutSequenceEvaluationResult result,
        ShortcutSequence? matchedSequence,
        IReadOnlyList<ShortcutSequenceProgress> nextCandidates)
    {
        Result = result;
        MatchedSequence = matchedSequence;
        NextCandidates = nextCandidates;
    }

    public ShortcutSequenceEvaluationResult Result { get; }
    public ShortcutSequence? MatchedSequence { get; }
    public IReadOnlyList<ShortcutSequenceProgress> NextCandidates { get; }

    public static ShortcutSequenceEvaluation None { get; } =
        new(ShortcutSequenceEvaluationResult.None, null, System.Array.Empty<ShortcutSequenceProgress>());

    public static ShortcutSequenceEvaluation Completed(ShortcutSequence sequence) =>
        new(ShortcutSequenceEvaluationResult.CompletedMatch, sequence, System.Array.Empty<ShortcutSequenceProgress>());

    public static ShortcutSequenceEvaluation Partial(IReadOnlyList<ShortcutSequenceProgress> nextCandidates) =>
        new(ShortcutSequenceEvaluationResult.PartialMatch, null, nextCandidates);
}
