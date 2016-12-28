﻿using MetaMorpheus;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndexSearchAndAnalyze
{
    public class AnalysisEngine : MyEngine
    {
        private AnalysisParams analysisParams;

        public AnalysisEngine(AnalysisParams analysisParams)
        {
            this.analysisParams = analysisParams;
        }

        protected override MyResults RunSpecific()
        {

            AddObservedPeptidesToDictionary(analysisParams.newPsms, analysisParams.compactPeptideToProteinPeptideMatching, analysisParams.proteinList, analysisParams.variableModifications, analysisParams.fixedModifications, analysisParams.localizeableModifications, analysisParams.protease);
            var fullSequenceToProteinSingleMatch = GetSingleMatchDictionary(analysisParams.compactPeptideToProteinPeptideMatching);
            
            for (int j = 0; j < analysisParams.searchModes.Count; j++)
            {
                PSMwithPeptide[] psmsWithPeptides = new PSMwithPeptide[analysisParams.newPsms.Length];

                Parallel.ForEach(Partitioner.Create(0, analysisParams.newPsms.Length), fff =>
                {
                    for (int i = fff.Item1; i < fff.Item2; i++)
                    {
                        if (analysisParams.newPsms[i] != null)
                        {
                            var huh = analysisParams.newPsms[i][j];
                            if (huh != null && huh.ScoreFromSearch >= 1)
                                psmsWithPeptides[i] = new PSMwithPeptide(huh, fullSequenceToProteinSingleMatch[huh.peptide], analysisParams.fragmentTolerance, analysisParams.myMsDataFile);
                        }
                    }
                });

                var orderedPsms = psmsWithPeptides.Where(b => b != null).OrderByDescending(b => b.ScoreFromSearch);

                var orderedPsmsWithFDR = DoFalseDiscoveryRateAnalysis(orderedPsms);
                var limitedpsms_with_fdr = orderedPsmsWithFDR.Where(b => (b.QValue <= 0.01)).ToList();
                if (limitedpsms_with_fdr.Where(b => !b.isDecoy).Count() > 0)
                {
                    var hm = MyAnalysis(limitedpsms_with_fdr);
                    analysisParams.onfinished1(hm);
                }

                analysisParams.onfinished2(orderedPsmsWithFDR);
            }

            return new AnalysisResults();
        }

        private static MyNewTreeStructure MyAnalysis(List<NewPsmWithFDR> limitedpsms_with_fdr)
        {
            MyNewTreeStructure myTreeStructure = new MyNewTreeStructure();
            myTreeStructure.GenerateBins(limitedpsms_with_fdr, 0.003);
            myTreeStructure.AddToBins(limitedpsms_with_fdr);

            Console.WriteLine("Identifying bins...");
            MyAnalysisClass.IdentifyUnimodBins(myTreeStructure, 0.003);
            MyAnalysisClass.IdentifyUniprotBins(myTreeStructure, 0.003);
            MyAnalysisClass.IdentifyAA(myTreeStructure, 0.003);

            Console.WriteLine("Identifying combos...");
            MyAnalysisClass.IdentifyCombos(myTreeStructure, 0.003);

            Console.WriteLine("Extracting residues from localizeable...");
            MyAnalysisClass.IdentifyResidues(myTreeStructure);

            Console.WriteLine("Identifying mods in common...");
            MyAnalysisClass.IdentifyMods(myTreeStructure);

            Console.WriteLine("Identifying AAs in common...");
            MyAnalysisClass.IdentifyAAsInCommon(myTreeStructure);

            Console.WriteLine("Identifying mine...");
            MyAnalysisClass.IdentifyMine(myTreeStructure, 0.003);



            Console.WriteLine("Done with my analysis analysis.. ");

            return myTreeStructure;
        }


        private static List<NewPsmWithFDR> DoFalseDiscoveryRateAnalysis(IEnumerable<PSMwithPeptide> items)
        {
            List<NewPsmWithFDR> ids = new List<NewPsmWithFDR>();

            int cumulative_target = 0;
            int cumulative_decoy = 0;
            foreach (PSMwithPeptide item in items)
            {
                var isDecoy = item.isDecoy;
                if (isDecoy)
                    cumulative_decoy++;
                else
                    cumulative_target++;
                double temp_q_value = (double)cumulative_decoy / (cumulative_target + cumulative_decoy);
                ids.Add(new NewPsmWithFDR(item, cumulative_target, cumulative_decoy, temp_q_value));
            }

            double min_q_value = double.PositiveInfinity;
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                NewPsmWithFDR id = ids[i];
                if (id.QValue > min_q_value)
                    id.QValue = min_q_value;
                else if (id.QValue < min_q_value)
                    min_q_value = id.QValue;
            }

            return ids;
        }
        private static void AddObservedPeptidesToDictionary(NewPsm[][] newPsms, Dictionary<CompactPeptide, HashSet<PeptideWithSetModifications>> fullSequenceToProteinPeptideMatching, List<Protein> proteinList, List<MorpheusModification> variableModifications, List<MorpheusModification> fixedModifications, List<MorpheusModification> localizeableModifications, Protease protease)
        {
            foreach (var ah in newPsms)
            {
                if (ah != null)
                    foreach (var fhh in ah)
                    {
                        if (fhh != null && !fullSequenceToProteinPeptideMatching.ContainsKey(fhh.peptide))
                            fullSequenceToProteinPeptideMatching.Add(fhh.peptide, new HashSet<PeptideWithSetModifications>());
                    }
            }

            foreach (var protein in proteinList)
                foreach (var peptide in protein.Digest(protease, 2, InitiatorMethionineBehavior.Variable).ToList())
                {
                    if (peptide.Length == 1 || peptide.Length > 252)
                        continue;
                    peptide.SetFixedModifications(fixedModifications);
                    var ListOfModifiedPeptides = peptide.GetPeptideWithSetModifications(variableModifications, 4098, 3, localizeableModifications).ToList();
                    foreach (var yyy in ListOfModifiedPeptides)
                    {
                        HashSet<PeptideWithSetModifications> v;
                        if (fullSequenceToProteinPeptideMatching.TryGetValue(new CompactPeptide(yyy, variableModifications, localizeableModifications), out v))
                        {
                            v.Add(yyy);
                        }
                    }
                }
        }

        private static Dictionary<CompactPeptide, PeptideWithSetModifications> GetSingleMatchDictionary(Dictionary<CompactPeptide, HashSet<PeptideWithSetModifications>> fullSequenceToProteinPeptideMatching)
        {
            // Right now very stupid, add the first decoy one, and if no decoy, add the first one
            Dictionary<CompactPeptide, PeptideWithSetModifications> outDict = new Dictionary<CompactPeptide, PeptideWithSetModifications>();
            foreach (var kvp in fullSequenceToProteinPeptideMatching)
            {
                bool sawDecoy = false;
                foreach (var entry in kvp.Value)
                {
                    if (entry.protein.isDecoy)
                    {
                        outDict[kvp.Key] = entry;
                        sawDecoy = true;
                        break;
                    }
                }
                if (sawDecoy == false)
                    outDict[kvp.Key] = kvp.Value.First();
            }
            return outDict;
        }


    }
}