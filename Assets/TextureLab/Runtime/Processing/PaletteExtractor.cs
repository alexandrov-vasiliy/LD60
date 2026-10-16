using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    /// <summary>
    /// Incremental, deterministic palette extraction operation. Hosts can call
    /// Step with a frame-sized sample budget and keep the UI responsive.
    /// </summary>
    public sealed class TextureLabPaletteExtractionOperation
    {
        private const int Iterations = 12;
        private readonly List<Vector3> samples;
        private readonly List<Vector3> centroids;
        private readonly float[] distances;
        private readonly Vector3[] sums;
        private readonly int[] counts;
        private int sampleIndex;
        private int iteration;
        private bool completed;
        private bool canceled;
        private List<Color> result;

        internal TextureLabPaletteExtractionOperation(Texture2D source, int requestedColorCount)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            samples = PaletteExtractor.ReadSamples(source);
            int colorCount = Mathf.Clamp(requestedColorCount, 1, Mathf.Min(64, Mathf.Max(1, samples.Count)));
            centroids = InitializeCentroids(samples, colorCount);
            distances = new float[samples.Count];
            sums = new Vector3[colorCount];
            counts = new int[colorCount];

            if (samples.Count == 0)
                Finish(new List<Color> { Color.black });
        }

        public bool IsCompleted => completed;
        public bool IsCanceled => canceled;
        public float Progress => completed ? 1f : Mathf.Clamp01((iteration + sampleIndex / (float)Mathf.Max(1, samples.Count)) / Iterations);
        public IReadOnlyList<Color> Result => result;

        public static TextureLabPaletteExtractionOperation Begin(Texture2D source, int requestedColorCount)
        {
            return new TextureLabPaletteExtractionOperation(source, requestedColorCount);
        }

        public bool Step(int sampleBudget)
        {
            if (completed || canceled)
                return true;

            int budget = Mathf.Max(1, sampleBudget);
            while (budget > 0 && !completed)
            {
                while (sampleIndex < samples.Count && budget-- > 0)
                {
                    int nearest = FindNearest(samples[sampleIndex], centroids, out float distance);
                    distances[sampleIndex] = distance;
                    sums[nearest] += samples[sampleIndex];
                    counts[nearest]++;
                    sampleIndex++;
                }

                if (sampleIndex < samples.Count)
                    continue;

                bool stable = UpdateCentroids();
                if (stable || iteration + 1 >= Iterations)
                {
                    Finish(BuildResult());
                    break;
                }

                iteration++;
                sampleIndex = 0;
                Array.Clear(sums, 0, sums.Length);
                Array.Clear(counts, 0, counts.Length);
            }

            return completed;
        }

        public void Cancel()
        {
            if (!completed)
                canceled = true;
        }

        private bool UpdateCentroids()
        {
            float largestMove = 0f;
            var selectedSamples = new HashSet<int>();
            for (int centroidIndex = 0; centroidIndex < centroids.Count; centroidIndex++)
            {
                Vector3 next;
                if (counts[centroidIndex] > 0)
                {
                    next = sums[centroidIndex] / counts[centroidIndex];
                }
                else
                {
                    int replacement = FindFarthestUnusedSample(selectedSamples);
                    next = samples[replacement];
                    selectedSamples.Add(replacement);
                }

                largestMove = Mathf.Max(largestMove, (centroids[centroidIndex] - next).sqrMagnitude);
                centroids[centroidIndex] = next;
            }

            return largestMove < 0.0000001f;
        }

        private int FindFarthestUnusedSample(HashSet<int> selectedSamples)
        {
            int largestIndex = 0;
            float largestDistance = float.MinValue;
            for (int index = 0; index < distances.Length; index++)
            {
                if (selectedSamples.Contains(index))
                    continue;
                if (distances[index] > largestDistance)
                {
                    largestDistance = distances[index];
                    largestIndex = index;
                }
            }

            return largestIndex;
        }

        private List<Color> BuildResult()
        {
            centroids.Sort((left, right) => left.x.CompareTo(right.x));
            var extracted = new List<Color>(centroids.Count);
            foreach (Vector3 centroid in centroids)
            {
                Color color = PaletteExtractor.OklabToLinearRgb(centroid);
                bool duplicate = extracted.Exists(existing => new Vector3(
                    existing.r - color.r,
                    existing.g - color.g,
                    existing.b - color.b).sqrMagnitude < 0.000001f);
                if (!duplicate)
                    extracted.Add(color);
            }

            return extracted.Count > 0 ? extracted : new List<Color> { Color.black };
        }

        private void Finish(List<Color> colors)
        {
            result = colors;
            completed = true;
            iteration = Iterations;
            sampleIndex = samples.Count;
        }

        private static List<Vector3> InitializeCentroids(IReadOnlyList<Vector3> sourceSamples, int colorCount)
        {
            if (sourceSamples.Count == 0)
                return new List<Vector3> { Vector3.zero };

            var random = new System.Random(PaletteExtractor.DeterministicSeed);
            var result = new List<Vector3>(colorCount) { sourceSamples[random.Next(sourceSamples.Count)] };
            var nearestDistances = new float[sourceSamples.Count];
            for (int index = 0; index < nearestDistances.Length; index++)
                nearestDistances[index] = float.MaxValue;

            while (result.Count < colorCount)
            {
                Vector3 newest = result[^1];
                int nextIndex = 0;
                float largestDistance = float.MinValue;
                for (int sampleIndex = 0; sampleIndex < sourceSamples.Count; sampleIndex++)
                {
                    float distance = (sourceSamples[sampleIndex] - newest).sqrMagnitude;
                    nearestDistances[sampleIndex] = Mathf.Min(nearestDistances[sampleIndex], distance);
                    if (nearestDistances[sampleIndex] > largestDistance)
                    {
                        largestDistance = nearestDistances[sampleIndex];
                        nextIndex = sampleIndex;
                    }
                }

                result.Add(sourceSamples[nextIndex]);
            }

            return result;
        }

        private static int FindNearest(Vector3 sample, IReadOnlyList<Vector3> centroids, out float nearestDistance)
        {
            int nearest = 0;
            nearestDistance = float.MaxValue;
            for (int index = 0; index < centroids.Count; index++)
            {
                float distance = (sample - centroids[index]).sqrMagnitude;
                if (distance >= nearestDistance)
                    continue;
                nearest = index;
                nearestDistance = distance;
            }

            return nearest;
        }
    }

    internal static class PaletteExtractor
    {
        private const int AnalysisMaxDimension = 128;
        internal const int DeterministicSeed = 0x544C;

        internal static List<Color> Extract(Texture2D source, int requestedColorCount)
        {
            TextureLabPaletteExtractionOperation operation = TextureLabPaletteExtractionOperation.Begin(source, requestedColorCount);
            while (!operation.Step(int.MaxValue))
            {
            }

            return new List<Color>(operation.Result);
        }

        internal static List<Vector3> ReadSamples(Texture2D source)
        {
            float scale = Mathf.Min(1f, AnalysisMaxDimension / (float)Mathf.Max(source.width, source.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            RenderTexture target = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                readable.Apply(false, false);

                Color[] pixels = readable.GetPixels();
                var samples = new List<Vector3>(pixels.Length);
                foreach (Color pixel in pixels)
                {
                    if (pixel.a >= 0.05f)
                        samples.Add(LinearRgbToOklab(pixel));
                }

                return samples;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                RuntimeObjectLifetime.Destroy(readable);
            }
        }

        internal static Vector3 LinearRgbToOklab(Color color)
        {
            float l = 0.4122214708f * color.r + 0.5363325363f * color.g + 0.0514459929f * color.b;
            float m = 0.2119034982f * color.r + 0.6806995451f * color.g + 0.1073969566f * color.b;
            float s = 0.0883024619f * color.r + 0.2817188376f * color.g + 0.6299787005f * color.b;
            float lRoot = Mathf.Pow(Mathf.Max(l, 0f), 1f / 3f);
            float mRoot = Mathf.Pow(Mathf.Max(m, 0f), 1f / 3f);
            float sRoot = Mathf.Pow(Mathf.Max(s, 0f), 1f / 3f);
            return new Vector3(
                0.2104542553f * lRoot + 0.793617785f * mRoot - 0.0040720468f * sRoot,
                1.9779984951f * lRoot - 2.428592205f * mRoot + 0.4505937099f * sRoot,
                0.0259040371f * lRoot + 0.7827717662f * mRoot - 0.808675766f * sRoot);
        }

        internal static Color OklabToLinearRgb(Vector3 lab)
        {
            float lRoot = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
            float mRoot = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
            float sRoot = lab.x - 0.0894841775f * lab.y - 1.291485548f * lab.z;
            float l = lRoot * lRoot * lRoot;
            float m = mRoot * mRoot * mRoot;
            float s = sRoot * sRoot * sRoot;
            return new Color(
                Mathf.Clamp01(4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s),
                Mathf.Clamp01(-1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s),
                Mathf.Clamp01(-0.0041960863f * l - 0.7034186147f * m + 1.707614701f * s),
                1f);
        }
    }
}
