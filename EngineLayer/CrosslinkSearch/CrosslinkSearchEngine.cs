﻿using Chemistry;
using MassSpectrometry;
using MzLibUtil;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Proteomics;

namespace EngineLayer.CrosslinkSearch
{
    public class CrosslinkSearchEngine : MetaMorpheusEngine
    {
        #region Private Fields

        private const double tolInDaForPreferringHavingMods = 0.03;

        private readonly List<int>[] fragmentIndex;

        private readonly Tolerance fragmentTolerance;

        private readonly float[] keys;

        private readonly Ms2ScanWithSpecificMass[] listOfSortedms2Scans;

        private readonly List<CompactPeptide> peptideIndex;

        //Crosslink parameters
        private CrosslinkerTypeClass crosslinker;
        private readonly int CrosslinkSearchTopNum;
        private readonly bool CrosslinkSearchWithCrosslinkerMod;
        private readonly Tolerance XLprecusorMsTl;
        private readonly Tolerance XLBetaPrecusorMsTl;

        private readonly List<ProductType> lp;
        private readonly Dictionary<ModificationWithMass, ushort> modsDictionary;

        #endregion Private Fields

        #region Public Constructors

        public CrosslinkSearchEngine(Ms2ScanWithSpecificMass[] listOfSortedms2Scans, List<CompactPeptide> peptideIndex, float[] keys, List<int>[] fragmentIndex, Tolerance fragmentTolerance, CrosslinkerTypeClass crosslinker, int CrosslinkSearchTopNum, bool CrosslinkSearchWithCrosslinkerMod, Tolerance XLprecusorMsTl, Tolerance XLBetaPrecusorMsTl, List<ProductType> lp, Dictionary<ModificationWithMass, ushort> modsDictionary, List<string> nestedIds) : base(nestedIds)
        {
            this.listOfSortedms2Scans = listOfSortedms2Scans;
            this.peptideIndex = peptideIndex;
            this.keys = keys;
            this.fragmentIndex = fragmentIndex;
            this.fragmentTolerance = fragmentTolerance;
            this.crosslinker = crosslinker;
            this.CrosslinkSearchTopNum = CrosslinkSearchTopNum;
            this.CrosslinkSearchWithCrosslinkerMod = CrosslinkSearchWithCrosslinkerMod;
            this.XLprecusorMsTl = XLprecusorMsTl;
            this.XLBetaPrecusorMsTl = XLBetaPrecusorMsTl;
            //AnalysisEngine parameters
            this.lp = lp;
            this.modsDictionary = modsDictionary;
        }

        #endregion Public Constructors

        #region Protected Methods

        protected override MetaMorpheusEngineResults RunSpecific()
        {
            MassDiffAcceptor XLsearchMode = new OpenSearchMode();

            Status("In crosslink search engine...", nestedIds);

            var listOfSortedms2ScansLength = listOfSortedms2Scans.Length;

            var outputObject = new object();
            int scansSeen = 0;
            int old_progress = 0;
            var peptideIndexCount = peptideIndex.Count;

            //Crosslink data storage
            List<Tuple<PsmCross, PsmCross>> newPsmsTopTuple = new List<Tuple<PsmCross, PsmCross>>();

            //Find Top matched peptides and then find Crosslinked peptides from them.
            //Parallel.ForEach(Partitioner.Create(0, 1), fff =>
            Parallel.ForEach(Partitioner.Create(0, listOfSortedms2ScansLength), fff =>
            {
                List<BestPeptideScoreNotch> bestPeptideScoreNotch = new List<BestPeptideScoreNotch>();
                double worstScores = new double();

                double[] fullPeptideScores = new double[peptideIndexCount];
                //Find the Top matched peptides
                for (int i = fff.Item1; i < fff.Item2; i++)
                {
                    var thisScan = listOfSortedms2Scans[i];
                    var thisScanprecursorMass = thisScan.PrecursorMass;
                    Array.Clear(fullPeptideScores, 0, peptideIndexCount);
                    CalculatePeptideScores(thisScan.TheScan, fullPeptideScores);
                    bestPeptideScoreNotch.Clear();
                    worstScores = 0;

                    for (int possibleWinningPeptideIndex = 0; possibleWinningPeptideIndex < fullPeptideScores.Length; possibleWinningPeptideIndex++)
                    {
                        var consideredScore = fullPeptideScores[possibleWinningPeptideIndex];
                        CompactPeptide candidatePeptide = peptideIndex[possibleWinningPeptideIndex];

                        if (consideredScore >= 1)
                        {

                            // Check if makes sense to add due to peptidescore!
                            //currentWorstScore to mark the current worst score and peptide for comparation and removal.
                            double currentWorstScore = worstScores;
                            //From all scored peptides to choose the Top Num ones
                            #region
                            if (bestPeptideScoreNotch != null && bestPeptideScoreNotch.Count == CrosslinkSearchTopNum)
                            {
                                // Full! Need to compare with old worst match
                                if (Math.Abs(currentWorstScore - consideredScore) < 1e-9)
                                {
                                    // Score is same as the worst, need to see if accepts and if prefer the new one
                                    int notch = XLsearchMode.Accepts(thisScanprecursorMass, candidatePeptide.MonoisotopicMassIncludingFixedMods);
                                    if (notch >= 0)
                                    {
                                        bestPeptideScoreNotch.RemoveAt(CrosslinkSearchTopNum - 1);
                                        bestPeptideScoreNotch.Add(new BestPeptideScoreNotch(candidatePeptide, consideredScore, notch));
                                        bestPeptideScoreNotch = bestPeptideScoreNotch.OrderByDescending(p => p.BestScore).ToList();
                                        worstScores = bestPeptideScoreNotch.Last().BestScore;
                                    }
                                }
                                else if (currentWorstScore < consideredScore)
                                {
                                    // Score is better than the worst, only make sure it is acceptable
                                    int notch = XLsearchMode.Accepts(thisScanprecursorMass, candidatePeptide.MonoisotopicMassIncludingFixedMods);
                                    if (notch >= 0)
                                    {
                                        bestPeptideScoreNotch.RemoveAt(CrosslinkSearchTopNum - 1);
                                        bestPeptideScoreNotch.Add(new BestPeptideScoreNotch(candidatePeptide, consideredScore, notch));
                                        bestPeptideScoreNotch = bestPeptideScoreNotch.OrderByDescending(p => p.BestScore).ToList();
                                        worstScores = bestPeptideScoreNotch.Last().BestScore;
                                    }
                                }
                            }
                            // Did not exist! Only make sure that it is acceptable.
                            else
                            {
                                int notch = XLsearchMode.Accepts(thisScanprecursorMass, candidatePeptide.MonoisotopicMassIncludingFixedMods);
                                if (notch >= 0)
                                {
                                    if (bestPeptideScoreNotch == null)
                                    {
                                        bestPeptideScoreNotch = new List<BestPeptideScoreNotch>();
                                    }
                                    bestPeptideScoreNotch.Add(new BestPeptideScoreNotch(candidatePeptide, consideredScore, notch));
                                    bestPeptideScoreNotch = bestPeptideScoreNotch.OrderByDescending(p => p.BestScore).ToList();
                                    worstScores = bestPeptideScoreNotch.Last().BestScore;
                                }
                            }
                            #endregion

                        }
                    }

                    //Create parameters to store current Crosslinked psm data?

                    if (bestPeptideScoreNotch != null)
                    {
                        //Function that find the two crosslinked peptide
                        var crosslinkPeptidePairList = FindCrosslinkedPeptide(thisScan, bestPeptideScoreNotch, i);

                        if (crosslinkPeptidePairList != null)
                        {
                            newPsmsTopTuple.Add(crosslinkPeptidePairList);
                        }
                    }

                }
                lock (outputObject)
                {
                    scansSeen += fff.Item2 - fff.Item1;
                    var new_progress = (int)((double)scansSeen / (listOfSortedms2ScansLength) * 100);
                    if (new_progress > old_progress)
                    {
                        ReportProgress(new ProgressEventArgs(new_progress, "In Crosslink search loop", nestedIds));
                        old_progress = new_progress;
                    }
                }
            });
            return new CrosslinkSearchResults(newPsmsTopTuple, this);
        }

        #endregion Protected Methods

        #region Private Methods

        private void CalculatePeptideScores(IMsDataScan<IMzSpectrum<IMzPeak>> spectrum, double[] peptideScores)
        {
            for (int i = 0; i < spectrum.MassSpectrum.Size; i++)
            {
                var theAdd = 1 + spectrum.MassSpectrum[i].Intensity / spectrum.TotalIonCurrent;
                var experimentalPeakInDaltons = spectrum.MassSpectrum[i].Mz - Constants.protonMass;
                float closestPeak;
                var ipos = Array.BinarySearch(keys, (float)experimentalPeakInDaltons);
                if (ipos < 0)
                    ipos = ~ipos;

                if (ipos > 0)
                {
                    var downIpos = ipos - 1;
                    // Try down
                    while (downIpos >= 0)
                    {
                        closestPeak = keys[downIpos];
                        if (fragmentTolerance.Within(experimentalPeakInDaltons, closestPeak))
                        {
                            foreach (var heh in fragmentIndex[downIpos])
                                peptideScores[heh] += theAdd;
                        }
                        else
                            break;
                        downIpos--;
                    }
                }
                if (ipos < keys.Length)
                {
                    var upIpos = ipos;
                    // Try here and up
                    while (upIpos < keys.Length)
                    {
                        closestPeak = keys[upIpos];
                        if (fragmentTolerance.Within(experimentalPeakInDaltons, closestPeak))
                        {
                            foreach (var heh in fragmentIndex[upIpos])
                                peptideScores[heh] += theAdd;
                        }
                        else
                            break;
                        upIpos++;
                    }
                }
            }
        }

        //Targetting function: to find two peptides that in the Top matched peptides
        private Tuple<PsmCross, PsmCross> FindCrosslinkedPeptide(Ms2ScanWithSpecificMass theScan, List<BestPeptideScoreNotch> theScanBestPeptide, int i)
        {
            Tuple<PsmCross, PsmCross> bestPsmCrossList = null;
            for (int ind = 0; ind < theScanBestPeptide.Count; ind++)
            {

                var x = theScanBestPeptide[ind].BestPeptide.MonoisotopicMassIncludingFixedMods;
                for (int inx = ind; inx < theScanBestPeptide.Count; inx++)
                {
                    var y = theScanBestPeptide[inx].BestPeptide.MonoisotopicMassIncludingFixedMods;
                    if (XLprecusorMsTl.Within(theScan.PrecursorMass, x + y + crosslinker.TotalMass))
                    {
                        var psmCrossAlpha = new PsmCross(theScanBestPeptide[ind].BestPeptide, theScanBestPeptide[ind].BestNotch, theScanBestPeptide[ind].BestScore, i, theScan);
                        var psmCrossBeta = new PsmCross(theScanBestPeptide[inx].BestPeptide, theScanBestPeptide[inx].BestNotch, theScanBestPeptide[inx].BestScore, i, theScan);
                        psmCrossAlpha.topPosition = new int[] { ind, inx };
                        XLCalculateTotalProductMassesMightHave(theScan, psmCrossAlpha);
                        XLCalculateTotalProductMassesMightHave(theScan, psmCrossBeta);
                        psmCrossAlpha.XLTotalScore = psmCrossAlpha.XLBestScore + psmCrossBeta.XLBestScore;

                        if (bestPsmCrossList == null)
                        {
                            bestPsmCrossList = new Tuple<PsmCross, PsmCross>(psmCrossAlpha, psmCrossBeta);
                        }
                        else
                        {
                            if (bestPsmCrossList.Item1.XLTotalScore < psmCrossAlpha.XLTotalScore)
                            {
                                bestPsmCrossList = new Tuple<PsmCross, PsmCross>(psmCrossAlpha, psmCrossBeta);
                            }
                        }                       
                    }
                }
            }
            return bestPsmCrossList;
        }

        private void XLCalculateTotalProductMassesMightHave(Ms2ScanWithSpecificMass theScan, PsmCross psmCross)
        {
            var modMass = theScan.PrecursorMass - psmCross.CompactPeptide.MonoisotopicMassIncludingFixedMods - crosslinker.TotalMass;
            int length = psmCross.CompactPeptide.NTerminalMasses.Length;
            var pmmh = psmCross.ProductMassesMightHaveDuplicatesAndNaNs(lp);
            ProductMassesMightHave pmmhTop = new ProductMassesMightHave();
            //int pos = -1;
            List<ProductMassesMightHave> pmmhList = new List<ProductMassesMightHave>();

            var linkPos = PsmCross.xlPosCal(psmCross.CompactPeptide, crosslinker);
            linkPos.Add(0);
            foreach (var ipos in linkPos)
            {
                //pos = ipos;
                ProductMassesMightHave pmmhCurr = new ProductMassesMightHave();
                pmmhCurr.xlpos = ipos;
                List<double> x = new List<double>();
                List<string> y = new List<string>();
                if (crosslinker.Cleavable)
                {
                    x.Add(theScan.PrecursorMass - modMass - crosslinker.CleaveMassLong);
                    y.Add("PepS");
                    x.Add(theScan.PrecursorMass - modMass - crosslinker.CleaveMassShort);
                    y.Add("PepL");
                }
                for (int i = 0; i < pmmh.ProductMz.Length; i++)
                {
                    var cr = pmmh.ProductName[i][0];
                    var nm = Int32.Parse(System.Text.RegularExpressions.Regex.Match(pmmh.ProductName[i], @"\d+").Value);                    
                    if ((cr == 'b' || cr == 'c') && nm < ipos + 1)
                    {
                        x.Add(pmmh.ProductMz[i]);
                        y.Add(pmmh.ProductName[i]);
                    }
                    if ((cr == 'y' || cr == 'z') && nm < length - ipos + 1)
                    {
                        x.Add(pmmh.ProductMz[i]);
                        y.Add(pmmh.ProductName[i]);
                    }
                    if (cr == 'b' && nm >= ipos + 1)
                    {
                        x.Add(pmmh.ProductMz[i] + modMass + crosslinker.TotalMass);
                        y.Add("t1b" + nm.ToString());

                        x.Add((pmmh.ProductMz[i] + modMass + crosslinker.TotalMass) / 2);
                        y.Add("t2b" + nm.ToString());

                        if (crosslinker.Cleavable)
                        {
                            x.Add(pmmh.ProductMz[i] + crosslinker.CleaveMassShort);
                            y.Add("sb" + nm.ToString());

                            x.Add(pmmh.ProductMz[i] + crosslinker.CleaveMassLong);
                            y.Add("lb" + nm.ToString());
                        }
                    }

                    if (cr == 'c' && nm >= ipos)
                    {
                        x.Add(pmmh.ProductMz[i] + modMass + crosslinker.TotalMass);
                        y.Add("t1c" + nm.ToString());

                        x.Add((pmmh.ProductMz[i] + modMass + crosslinker.TotalMass) / 2);
                        y.Add("t2c" + nm.ToString());

                        if (crosslinker.Cleavable)
                        {
                            x.Add(pmmh.ProductMz[i] + crosslinker.CleaveMassShort);
                            y.Add("sc" + nm.ToString());

                            x.Add(pmmh.ProductMz[i] + crosslinker.CleaveMassLong);
                            y.Add("lc" + nm.ToString());
                        }
                    }

                    if (cr == 'y' && (nm >= length - ipos + 1))
                    {
                        x.Add(pmmh.ProductMz[i] + modMass + crosslinker.TotalMass);
                        y.Add("t1y" + nm.ToString());

                        x.Add((pmmh.ProductMz[i] + modMass + crosslinker.TotalMass) / 2);
                        y.Add("t2y" + nm.ToString());

                        if (crosslinker.Cleavable)
                        {
                            x.Add(pmmh.ProductMz[i] + crosslinker.CleaveMassShort);
                            y.Add("sy" + nm.ToString());

                            x.Add(pmmh.ProductMz[i] + crosslinker.CleaveMassLong);
                            y.Add("ly" + nm.ToString());
                        }
                    }

                    if (cr == 'z' && (nm >= length - ipos + 1))
                    {
                        x.Add(pmmh.ProductMz[i] + modMass + crosslinker.TotalMass);
                        y.Add("t1z" + nm.ToString());

                        x.Add((pmmh.ProductMz[i] + modMass + crosslinker.TotalMass) / 2);
                        y.Add("t2z" + nm.ToString());

                        if (crosslinker.Cleavable)
                        {
                            x.Add(pmmh.ProductMz[i] + crosslinker.CleaveMassShort);
                            y.Add("sz" + nm.ToString());

                            x.Add(pmmh.ProductMz[i] + crosslinker.CleaveMassLong);
                            y.Add("lz" + nm.ToString());
                        }
                    }
                }
                pmmhCurr.ProductMz = x.ToArray();
                pmmhCurr.ProductName = y.ToArray();
                Array.Sort(pmmhCurr.ProductMz, pmmhCurr.ProductName);
                pmmhList.Add(pmmhCurr);
            }

            List<double> scoreList = new List<double>();
            List<MatchedIonInfo> miil = new List<MatchedIonInfo>();
            foreach (var pmm in pmmhList)
            {
                var matchedIonMassesListPositiveIsMatch = new MatchedIonInfo(pmm.ProductMz.Length);
                double pmmScore = PsmCross.XLMatchIons(theScan.TheScan, fragmentTolerance, pmm.ProductMz, pmm.ProductName, matchedIonMassesListPositiveIsMatch);
                miil.Add(matchedIonMassesListPositiveIsMatch);
                scoreList.Add(pmmScore);
            }

            psmCross.XLBestScore = scoreList.Max();
            psmCross.matchedIonInfo = miil[scoreList.IndexOf(scoreList.Max())];
            psmCross.xlpos = pmmhList[scoreList.IndexOf(scoreList.Max())].xlpos + 1;
        }

        #endregion Private Methods
    }
}

