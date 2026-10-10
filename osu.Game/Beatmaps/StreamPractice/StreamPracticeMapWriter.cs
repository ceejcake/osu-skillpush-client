// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Beatmaps.StreamPractice
{
    /// <summary>
    /// Writes an osu! beatmap which is a stream practice pattern, following the timing of another beatmap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A continuous stream follows the "Extremely Long Stream Practice" beatmaps: 1/4 notes, 64 to a lap of a circle in the
    /// middle of the playfield (one lap every 16 beats), going clockwise from the top. Each bar starts a new combo with a
    /// finish, and each beat has a clap.
    /// </para>
    /// <para>
    /// Bursts follow the "Burst Stream Practice Maps": 1/4 notes, 16 to a side of a square going clockwise from its top left
    /// corner, with a new combo at each corner and a whistle on each beat. Each burst is followed by a rest of the same
    /// length, filled with short repeating sliders, one to a beat, which carry on round the square so that the rhythm and
    /// the movement don't stop.
    /// </para>
    /// <para>
    /// Both begin on the first bar after the beatmap's first object, and end with its last.
    /// </para>
    /// </remarks>
    public static class StreamPracticeMapWriter
    {
        public const string ARTIST = "Various Artists";
        public const string TITLE = "Stream Practice";

        /// <summary>
        /// A tag on every generated beatmap, by which the set they go into is found.
        /// </summary>
        public const string MARKER_TAG = "stream-practice-generated";

        /// <summary>
        /// Notes per beat.
        /// </summary>
        public const int SNAP_DIVISOR = 4;

        /// <summary>
        /// Notes per lap of the circle, or of the square.
        /// </summary>
        public const int NOTES_PER_LAP = 64;

        public const double CIRCLE_CENTRE_X = 255.5;
        public const double CIRCLE_CENTRE_Y = 192.5;
        public const double CIRCLE_RADIUS = 106.5;

        public const int SQUARE_LEFT = 128;
        public const int SQUARE_TOP = 64;
        public const int SQUARE_SPACING = 16;

        /// <summary>
        /// Notes along each side of the square.
        /// </summary>
        public const int SQUARE_SIDE = NOTES_PER_LAP / 4;

        /// <summary>
        /// How far the path of a rest slider is drawn, and how much of it is used, in osu!pixels. Together with the slider
        /// velocity and repeats, this makes each slider last three quarters of a beat.
        /// </summary>
        private const int rest_slider_path = 48;

        private const int rest_slider_length = 35;
        private const int rest_slider_slides = 3;

        private const double slider_multiplier = 1.4;

        private const int hit_sound_whistle = 2;
        private const int hit_sound_finish = 4;
        private const int hit_sound_clap = 8;

        private const int type_circle = 1;
        private const int type_slider = 2;
        private const int type_new_combo = 4;

        /// <summary>
        /// A note of the generated stream.
        /// </summary>
        /// <param name="Time">When the note is hit.</param>
        /// <param name="X">The note's position across the playfield.</param>
        /// <param name="Y">The note's position down the playfield.</param>
        /// <param name="NewCombo">Whether the note starts a combo.</param>
        /// <param name="OnBeat">Whether the note falls on a beat.</param>
        /// <param name="SliderEnd">For a rest slider, the end of its path. Otherwise <see langword="null"/>, for a circle.</param>
        public readonly record struct Note(int Time, int X, int Y, bool NewCombo, bool OnBeat, (int X, int Y)? SliderEnd = null)
        {
            public bool IsSlider => SliderEnd != null;
        }

        /// <summary>
        /// The name given to the difficulty generated from a beatmap.
        /// </summary>
        public static string GetDifficultyName(IBeatmap source, StreamPracticeOptions options)
        {
            double beatLength = source.GetMostCommonBeatLength();
            int bpm = beatLength > 0 ? (int)Math.Round(60000 / beatLength) : 0;

            string name = $"{source.Metadata.Title} - [{bpm} BPM]";

            // A continuous stream has no burst length to give.
            return options.Pattern == StreamPracticePattern.Bursts ? $"{name} - {options.BurstLength} notes" : name;
        }

        /// <summary>
        /// Works out the notes for a beatmap, running from its first object to its last.
        /// </summary>
        public static IReadOnlyList<Note> CreateNotes(IBeatmap source, StreamPracticeOptions options)
        {
            if (options.Pattern == StreamPracticePattern.Bursts && (options.BurstLength < SNAP_DIVISOR || options.BurstLength % SNAP_DIVISOR != 0))
                throw new ArgumentException($"Bursts have to be a whole number of beats long, not {options.BurstLength} notes.", nameof(options));

            if (source.HitObjects.Count == 0)
                throw new InvalidOperationException("The beatmap has no objects, so there's nothing to take the length of the stream from.");

            var timingPoints = source.ControlPointInfo.TimingPoints.Where(t => t.BeatLength > 0).OrderBy(t => t.Time).ToList();

            if (timingPoints.Count == 0)
                throw new InvalidOperationException("The beatmap has no timing points.");

            double start = source.HitObjects.Min(h => h.StartTime);
            double end = source.HitObjects.Max(h => h.GetEndTime());

            var notes = new List<Note>();

            // How far round the circle or square the stream has got. This carries on across changes of timing, and counts
            // the rests between bursts, so that the stream keeps moving through them.
            int position = 0;

            for (int i = 0; i < timingPoints.Count; i++)
            {
                var timingPoint = timingPoints[i];

                // Each timing point covers the time until the next one, apart from the first, which also covers anything
                // before it.
                double sectionEnd = i + 1 < timingPoints.Count ? timingPoints[i + 1].Time : double.PositiveInfinity;

                if (sectionEnd <= start)
                    continue;

                double tickLength = timingPoint.BeatLength / SNAP_DIVISOR;
                int ticksPerBar = SNAP_DIVISOR * Math.Max(1, timingPoint.TimeSignature.Numerator);

                long firstTick = (long)Math.Ceiling((Math.Max(start, timingPoint.Time) - timingPoint.Time) / tickLength - 1e-6);

                // The stream begins on a bar, so that it lines up with the music.
                if (position == 0)
                    firstTick = ceilingToMultiple(firstTick, ticksPerBar);

                for (long tick = firstTick;; tick++)
                {
                    double time = timingPoint.Time + tick * tickLength;

                    if (time >= sectionEnd - 1e-6 || time > end + 1e-6)
                        break;

                    bool onBeat = modulo(tick, SNAP_DIVISOR) == 0;

                    if (options.Pattern == StreamPracticePattern.Continuous)
                    {
                        var (x, y) = circlePosition(position);

                        notes.Add(new Note(roundTime(time), x, y, notes.Count == 0 || modulo(tick, ticksPerBar) == 0, onBeat));
                    }
                    else
                    {
                        // Every section starts with a burst, whatever the previous one finished on.
                        bool resting = modulo(tick - firstTick, 2L * options.BurstLength) >= options.BurstLength;
                        bool newCombo = notes.Count == 0 || position % SQUARE_SIDE == 0;
                        var (x, y) = squarePosition(position);

                        if (!resting)
                            notes.Add(new Note(roundTime(time), x, y, newCombo, onBeat));
                        else if (onBeat && restSliderFits(time, tickLength, sectionEnd, end))
                        {
                            var (directionX, directionY) = squareDirection(position);
                            notes.Add(new Note(roundTime(time), x, y, newCombo, onBeat, (x + directionX * rest_slider_path, y + directionY * rest_slider_path)));
                        }
                    }

                    position++;
                }

                if (sectionEnd > end)
                    break;
            }

            return notes;
        }

        /// <summary>
        /// Writes the generated beatmap, in the .osu format.
        /// </summary>
        /// <param name="source">The beatmap whose timing and difficulty are followed.</param>
        /// <param name="options">How the stream is laid out.</param>
        /// <param name="audioFile">The name the audio track has in the generated beatmap's set.</param>
        /// <param name="backgroundFile">The name the background has in the generated beatmap's set, if there is one.</param>
        /// <param name="creator">The name to credit with making the beatmap.</param>
        public static string Write(IBeatmap source, StreamPracticeOptions options, string audioFile, string? backgroundFile, string creator)
        {
            var notes = CreateNotes(source, options);
            var metadata = source.Metadata;
            var difficulty = source.Difficulty;

            var text = new StringBuilder();

            text.AppendLine("osu file format v14");
            text.AppendLine();

            text.AppendLine("[General]");
            text.AppendLine($"AudioFilename: {audioFile}");
            text.AppendLine("AudioLeadIn: 0");
            text.AppendLine(invariant($"PreviewTime: {metadata.PreviewTime}"));
            text.AppendLine("Countdown: 0");
            text.AppendLine("SampleSet: Soft");
            text.AppendLine("StackLeniency: 0.7");
            text.AppendLine("Mode: 0");
            text.AppendLine("LetterboxInBreaks: 0");
            text.AppendLine("WidescreenStoryboard: 1");
            text.AppendLine();

            text.AppendLine("[Editor]");
            text.AppendLine(invariant($"BeatDivisor: {SNAP_DIVISOR}"));
            text.AppendLine();

            text.AppendLine("[Metadata]");
            text.AppendLine($"Title:{TITLE}");
            text.AppendLine($"TitleUnicode:{TITLE}");
            text.AppendLine($"Artist:{ARTIST}");
            text.AppendLine($"ArtistUnicode:{ARTIST}");
            text.AppendLine($"Creator:{creator}");
            text.AppendLine($"Version:{GetDifficultyName(source, options)}");
            text.AppendLine($"Source:{metadata.Artist} - {metadata.Title}");
            text.AppendLine($"Tags:{MARKER_TAG} stream practice {metadata.Artist} {metadata.Title}");
            text.AppendLine("BeatmapID:0");
            text.AppendLine("BeatmapSetID:-1");
            text.AppendLine();

            text.AppendLine("[Difficulty]");
            text.AppendLine(invariant($"HPDrainRate:{difficulty.DrainRate}"));
            text.AppendLine(invariant($"CircleSize:{difficulty.CircleSize}"));
            text.AppendLine(invariant($"OverallDifficulty:{difficulty.OverallDifficulty}"));
            text.AppendLine(invariant($"ApproachRate:{difficulty.ApproachRate}"));
            text.AppendLine(invariant($"SliderMultiplier:{slider_multiplier}"));
            text.AppendLine("SliderTickRate:1");
            text.AppendLine();

            text.AppendLine("[Events]");
            if (!string.IsNullOrEmpty(backgroundFile))
                text.AppendLine($"0,0,\"{backgroundFile}\",0,0");
            text.AppendLine();

            text.AppendLine("[TimingPoints]");
            foreach (var timingPoint in source.ControlPointInfo.TimingPoints.Where(t => t.BeatLength > 0))
                text.AppendLine(invariant($"{timingPoint.Time:R},{timingPoint.BeatLength:R},{timingPoint.TimeSignature.Numerator},2,0,75,1,0"));
            text.AppendLine();

            text.AppendLine("[HitObjects]");

            foreach (var note in notes)
                text.AppendLine(options.Pattern == StreamPracticePattern.Continuous ? writeCircleStreamNote(note) : writeBurstNote(note));

            return text.ToString();
        }

        private static string writeCircleStreamNote(Note note)
        {
            int type = type_circle | (note.NewCombo ? type_new_combo : 0);
            int hitSound = (note.OnBeat ? hit_sound_clap : 0) | (note.NewCombo ? hit_sound_finish : 0);

            return invariant($"{note.X},{note.Y},{note.Time},{type},{hitSound},2:0:0:0:");
        }

        private static string writeBurstNote(Note note)
        {
            int newCombo = note.NewCombo ? type_new_combo : 0;
            int hitSound = note.OnBeat ? hit_sound_whistle : 0;

            if (note.SliderEnd is not (int endX, int endY))
                return invariant($"{note.X},{note.Y},{note.Time},{type_circle | newCombo},{hitSound},0:0:0:0:");

            // The whistle falls on the head, which is on the beat. The repeats and tail are left quiet.
            return invariant($"{note.X},{note.Y},{note.Time},{type_slider | newCombo},0,L|{endX}:{endY},{rest_slider_slides},{rest_slider_length},{hitSound}|0|0|0,0:0|0:0|0:0|0:0,0:0:0:0:");
        }

        /// <summary>
        /// Whether a rest slider starting at <paramref name="time"/> finishes within its timing section and the stream.
        /// </summary>
        private static bool restSliderFits(double time, double tickLength, double sectionEnd, double end)
        {
            double sliderEnd = time + (SNAP_DIVISOR - 1) * tickLength;
            return sliderEnd < sectionEnd - 1e-6 && sliderEnd <= end + 1e-6;
        }

        /// <summary>
        /// The position of a note on the circle. The first is one step clockwise from the top, and every lap ends at the top.
        /// </summary>
        private static (int x, int y) circlePosition(int index)
        {
            double angle = (index % NOTES_PER_LAP + 1) * 2 * Math.PI / NOTES_PER_LAP;

            return ((int)Math.Round(CIRCLE_CENTRE_X + CIRCLE_RADIUS * Math.Sin(angle)), (int)Math.Round(CIRCLE_CENTRE_Y - CIRCLE_RADIUS * Math.Cos(angle)));
        }

        /// <summary>
        /// The position of a note on the square, going clockwise from the top left corner.
        /// </summary>
        private static (int x, int y) squarePosition(int index)
        {
            int along = index % SQUARE_SIDE * SQUARE_SPACING;
            const int side_length = SQUARE_SIDE * SQUARE_SPACING;

            switch (index % NOTES_PER_LAP / SQUARE_SIDE)
            {
                case 0:
                    return (SQUARE_LEFT + along, SQUARE_TOP);

                case 1:
                    return (SQUARE_LEFT + side_length, SQUARE_TOP + along);

                case 2:
                    return (SQUARE_LEFT + side_length - along, SQUARE_TOP + side_length);

                default:
                    return (SQUARE_LEFT, SQUARE_TOP + side_length - along);
            }
        }

        /// <summary>
        /// The direction the square goes in at a note.
        /// </summary>
        private static (int x, int y) squareDirection(int index)
        {
            switch (index % NOTES_PER_LAP / SQUARE_SIDE)
            {
                case 0:
                    return (1, 0);

                case 1:
                    return (0, 1);

                case 2:
                    return (-1, 0);

                default:
                    return (0, -1);
            }
        }

        private static int roundTime(double time) => (int)Math.Round(time, MidpointRounding.AwayFromZero);

        private static long ceilingToMultiple(long value, long multiple) => (long)Math.Ceiling((double)value / multiple) * multiple;

        private static long modulo(long value, long divisor) => ((value % divisor) + divisor) % divisor;

        private static string invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
    }
}
