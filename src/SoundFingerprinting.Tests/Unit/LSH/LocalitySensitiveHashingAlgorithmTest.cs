﻿namespace SoundFingerprinting.Tests.Unit.LSH
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;

    using SoundFingerprinting.Configuration;
    using SoundFingerprinting.Data;
    using SoundFingerprinting.DAO;
    using SoundFingerprinting.InMemory;
    using SoundFingerprinting.LSH;
    using SoundFingerprinting.Math;

    [TestFixture]
    public class LocalitySensitiveHashingAlgorithmTest
    {
        private readonly ISimilarityUtility similarity = new SimilarityUtility();

        [Test]
        public void FingerprintsCantMatchUniformlyAtRandom()
        {
            var lshAlgorithm = LocalitySensitiveHashingAlgorithm.Instance;

            var random = new Random();

            var storage = new RAMStorage(25, new IntModelReferenceProvider());
            
            float one = 8192f / 5512;
            var config = new DefaultHashingConfig { NumberOfLSHTables = 25, NumberOfMinHashesPerTable = 4, HashBuckets = 0 };
            
            var track = new ModelReference<int>(1);
            for (int i = 0; i < 100; ++i)
            {
                var schema = TestUtilities.GenerateRandomFingerprint(random, 200, 128, 32);
                var hash = lshAlgorithm.Hash(new Fingerprint(schema, i * one, (uint)i), config, Enumerable.Empty<string>());
                storage.AddHashedFingerprint(hash, track);
            }

            for (int i = 0; i < 10; ++i)
            {
                var schema = TestUtilities.GenerateRandomFingerprint(random, 200, 128, 32);
                var hash = lshAlgorithm.Hash(new Fingerprint(schema, i * one, (uint)i), config, Enumerable.Empty<string>());
                for (int j = 0; j < 25; ++j)
                {
                    var ids = storage.GetSubFingerprintsByHashTableAndHash(j, hash.HashBins[j]);
                    Assert.IsFalse(ids.Any());
                }
            }
        }

        [Test]
        public void DistributionOfHashesHasToBeUniform()
        {
            var lshAlgorithm = LocalitySensitiveHashingAlgorithm.Instance;

            var random = new Random();

            var storage = new RAMStorage(25, new IntModelReferenceProvider());
            
            float one = 8192f / 5512;
            var config = new DefaultHashingConfig { NumberOfLSHTables = 25, NumberOfMinHashesPerTable = 4, HashBuckets = 0 };
            
            var track = new ModelReference<int>(1);
            int l = 100000;
            for (int i = 0; i < l; ++i)
            {
                var schema = TestUtilities.GenerateRandomFingerprint(random, 200, 128, 32);
                var hash = lshAlgorithm.Hash(new Fingerprint(schema, i * one, (uint)i), config, Enumerable.Empty<string>());
                storage.AddHashedFingerprint(hash, track);
            }

            var distribution = storage.HashCountsPerTable;

            foreach (var hashPerTable in distribution)
            {
                double collisions = (double) (l - hashPerTable) / l;
                Assert.IsTrue(collisions <= 0.01d, $"Less than 1% of collisions across 100K hashes: {collisions}");
            }
        }

        [Test]
        public void ShouldBeAbleToControlReturnedCandidatesWithThresholdParameter()
        {
            int l = 25, k = 4, width = 128, height = 72;

            var hashingConfig = new DefaultHashingConfig
                                {
                                    Width = width, Height = height, NumberOfLSHTables = l, NumberOfMinHashesPerTable = k
                                };

            var lsh = LocalitySensitiveHashingAlgorithm.Instance;

            double[] howSimilarly    = { 0.3, 0.5, 0.6, 0.7, 0.75, 0.8, 0.85, 0.9 };
            int[] expectedThresholds = { 0, 0, 0, 2, 3, 5, 7, 11 };

            const int simulations = 10000;
            Parallel.For(0, howSimilarly.Length, r =>    
            {
                var random = new Random((r + 1) * 100);
                double howSimilar = howSimilarly[r];
                int topWavelets = (int)(0.035 * width * height);
                var agreeOn = new List<int>();
                var hammingDistances = new List<int>();
                for (int i = 0; i < simulations; ++i)
                {
                    var fingerprints = TestUtilities.GenerateSimilarFingerprints(random, howSimilar, topWavelets, width * height * 2);
                    int hammingDistance = similarity.CalculateHammingDistance(fingerprints.Item1.ToBools(), fingerprints.Item2.ToBools());
                    hammingDistances.Add(hammingDistance);
                    var hashed1 = lsh.HashImage(new Fingerprint(fingerprints.Item1, 0, 0), hashingConfig, Enumerable.Empty<string>());
                    var hashed2 = lsh.HashImage(new Fingerprint(fingerprints.Item2, 0, 0), hashingConfig, Enumerable.Empty<string>());
                    int agreeCount = AgreeOn(hashed1.HashBins, hashed2.HashBins);
                    agreeOn.Add(agreeCount);
                }

                int requested = (int)((1 - howSimilar) * topWavelets * 2);
                Assert.AreEqual(requested, hammingDistances.Average(), 1);
                Assert.AreEqual(expectedThresholds[r], Math.Floor(agreeOn.Average()));
                Console.WriteLine($"Similarity: {howSimilar: 0.00}, Avg. Table Matches {agreeOn.Average(): 0.000}");
            });
        }

        private static int AgreeOn(int[] x, int[] y)
        {
            return x.Where((t, i) => t == y[i]).Count();
        }
    }
}
