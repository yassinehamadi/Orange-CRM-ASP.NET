using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using Newtonsoft.Json;
using IHM_Monitoring.Models;

namespace IHM_Monitoring.Controllers
{
    public class MonitoringController : Controller
    {
        private string ConnexionString =>
            ConfigurationManager.ConnectionStrings["MonitoringConnection"].ConnectionString;

        public ActionResult Index(DateTime? date)
        {
            var vm = new DashboardViewModel();

            using (var connexion = new SqlConnection(ConnexionString))
            {
                connexion.Open();

                // ── Bornes de dates ──────────────────────────────────────────────
                using (var cmdBornes = new SqlCommand("SELECT MIN(Date), MAX(Date) FROM MonitoringQuotidien", connexion))
                using (var reader = cmdBornes.ExecuteReader())
                {
                    reader.Read();
                    vm.DateMin = reader.GetDateTime(0);
                    vm.DateMax = reader.GetDateTime(1);
                }

                vm.DateAffichee = date ?? vm.DateMax;

                DateTime dateDebut30j  = vm.DateMax.AddDays(-29);
                DateTime dateDebut7j   = vm.DateMax.AddDays(-6);
                DateTime datePrevDebut = vm.DateMax.AddDays(-13);
                DateTime datePrevFin   = vm.DateMax.AddDays(-7);

                // ── 1. Tableau quotidien (requête existante, inchangée) ──────────
                string requeteJour = @"
                    SELECT Date, SubjectName, NombreReclamations, MoyenneMobile, ZScore, EstAnomalie
                    FROM MonitoringQuotidien
                    WHERE Date = @date
                    ORDER BY EstAnomalie DESC, NombreReclamations DESC";
                using (var cmd = new SqlCommand(requeteJour, connexion))
                {
                    cmd.Parameters.AddWithValue("@date", vm.DateAffichee);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            vm.TableauQuotidien.Add(new MonitoringQuotidien
                            {
                                Date               = reader.GetDateTime(0),
                                SubjectName        = reader.GetString(1),
                                NombreReclamations = reader.GetInt64(2),
                                MoyenneMobile      = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3),
                                ZScore             = reader.IsDBNull(4) ? (double?)null : reader.GetDouble(4),
                                EstAnomalie        = reader.GetBoolean(5)
                            });
                        }
                    }
                }

                // ── 2. KPIs — 30 derniers jours ─────────────────────────────────
                string requeteKpi = @"
                    SELECT
                        ISNULL(SUM(NombreReclamations), 0),
                        ISNULL(SUM(CASE WHEN EstAnomalie = 1 THEN 1 ELSE 0 END), 0)
                    FROM MonitoringQuotidien
                    WHERE Date >= @debut";
                using (var cmd = new SqlCommand(requeteKpi, connexion))
                {
                    cmd.Parameters.AddWithValue("@debut", dateDebut30j);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            vm.TotalReclamations = Convert.ToInt64(reader.GetValue(0));
                            vm.TotalAnomalies    = Convert.ToInt64(reader.GetValue(1));
                        }
                    }
                }

                // ── 3. Sujet le plus critique (toute la période) ─────────────────
                string requeteSujet = @"
                    SELECT TOP 1 SubjectName
                    FROM MonitoringQuotidien
                    WHERE EstAnomalie = 1
                    GROUP BY SubjectName
                    ORDER BY COUNT(*) DESC";
                using (var cmd = new SqlCommand(requeteSujet, connexion))
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                        vm.SujetCritique = reader.GetString(0);
                }

                // ── 4. Évolution 7 j courants vs 7 j précédents ──────────────────
                string requeteEvol = @"
                    SELECT
                        ISNULL(SUM(CASE WHEN Date >= @debut7j  THEN NombreReclamations ELSE 0 END), 0),
                        ISNULL(SUM(CASE WHEN Date >= @prevDebut AND Date <= @prevFin THEN NombreReclamations ELSE 0 END), 0)
                    FROM MonitoringQuotidien
                    WHERE Date >= @prevDebut";
                using (var cmd = new SqlCommand(requeteEvol, connexion))
                {
                    cmd.Parameters.AddWithValue("@debut7j",   dateDebut7j);
                    cmd.Parameters.AddWithValue("@prevDebut", datePrevDebut);
                    cmd.Parameters.AddWithValue("@prevFin",   datePrevFin);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            long semCourante   = Convert.ToInt64(reader.GetValue(0));
                            long semPrecedente = Convert.ToInt64(reader.GetValue(1));
                            if (semPrecedente > 0)
                            {
                                vm.Evolution           = Math.Round((double)(semCourante - semPrecedente) / semPrecedente * 100.0, 1);
                                vm.EvolutionPositive   = vm.Evolution >= 0;
                                vm.EvolutionDisponible = true;
                            }
                        }
                    }
                }

                // ── 5. Timeline — 30 derniers jours (pour Chart.js) ─────────────
                string requeteTimeline = @"
                    SELECT Date,
                           SUM(NombreReclamations) AS TotalRec,
                           SUM(CASE WHEN EstAnomalie = 1 THEN 1 ELSE 0 END) AS NbAnomalies
                    FROM MonitoringQuotidien
                    WHERE Date >= @debut
                    GROUP BY Date
                    ORDER BY Date";
                var tlDates = new List<string>();
                var tlRec   = new List<long>();
                var tlAno   = new List<long>();
                using (var cmd = new SqlCommand(requeteTimeline, connexion))
                {
                    cmd.Parameters.AddWithValue("@debut", dateDebut30j);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tlDates.Add(reader.GetDateTime(0).ToString("dd/MM"));
                            tlRec.Add(Convert.ToInt64(reader.GetValue(1)));
                            tlAno.Add(Convert.ToInt64(reader.GetValue(2)));
                        }
                    }
                }
                vm.EvolutionTimelineJson = JsonConvert.SerializeObject(
                    new { dates = tlDates, reclamations = tlRec, anomalies = tlAno });

                // ── 6. Top sujets par réclamations (toute la période) ─────────────
                string requeteTopSujets = @"
                    SELECT SubjectName, SUM(NombreReclamations) AS Total
                    FROM MonitoringQuotidien
                    GROUP BY SubjectName
                    ORDER BY Total DESC";
                var topSujetsRaw = new List<Tuple<string, long>>();
                long grandTotal  = 0;
                using (var cmd = new SqlCommand(requeteTopSujets, connexion))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long tot = Convert.ToInt64(reader.GetValue(1));
                        topSujetsRaw.Add(Tuple.Create(reader.GetString(0), tot));
                        grandTotal += tot;
                    }
                }
                foreach (var item in topSujetsRaw)
                {
                    vm.TopSujets.Add(new TopSujetItem
                    {
                        SubjectName       = item.Item1,
                        TotalReclamations = item.Item2,
                        Pourcentage       = grandTotal > 0
                                            ? Math.Round((double)item.Item2 / grandTotal * 100.0, 1)
                                            : 0
                    });
                }

                // ── 7. Top anomalies par nb de jours (toute la période) ───────────
                string requeteTopAno = @"
                    SELECT SubjectName, COUNT(*) AS NbJours
                    FROM MonitoringQuotidien
                    WHERE EstAnomalie = 1
                    GROUP BY SubjectName
                    ORDER BY NbJours DESC";
                var topAnoRaw = new List<Tuple<string, int>>();
                using (var cmd = new SqlCommand(requeteTopAno, connexion))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        topAnoRaw.Add(Tuple.Create(reader.GetString(0), reader.GetInt32(1)));
                }
                int maxAno = topAnoRaw.Count > 0 ? topAnoRaw[0].Item2 : 1;
                foreach (var item in topAnoRaw)
                {
                    vm.TopAnomalies.Add(new TopAnomalieItem
                    {
                        SubjectName     = item.Item1,
                        NbJoursAnomalie = item.Item2,
                        PourcentageBar  = maxAno > 0
                                          ? Math.Round((double)item.Item2 / maxAno * 100.0, 0)
                                          : 0
                    });
                }

                // ── 8. Tableau taux d'anomalie (toute la période) ─────────────────
                string requeteTaux = @"
                    SELECT SubjectName,
                           SUM(NombreReclamations) AS TotalRec,
                           SUM(CASE WHEN EstAnomalie = 1 THEN 1 ELSE 0 END) AS NbAnomalies,
                           COUNT(*) AS TotalJours
                    FROM MonitoringQuotidien
                    GROUP BY SubjectName
                    ORDER BY SUM(CASE WHEN EstAnomalie = 1 THEN 1 ELSE 0 END) DESC";
                using (var cmd = new SqlCommand(requeteTaux, connexion))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int nbAno    = Convert.ToInt32(reader.GetValue(2));
                        int totalJrs = reader.GetInt32(3);
                        vm.TableauTaux.Add(new TauxAnomalieItem
                        {
                            SubjectName       = reader.GetString(0),
                            TotalReclamations = Convert.ToInt64(reader.GetValue(1)),
                            NbJoursAnomalie   = nbAno,
                            TotalJours        = totalJrs,
                            TauxAnomalie      = totalJrs > 0
                                                ? Math.Round((double)nbAno / totalJrs * 100.0, 1)
                                                : 0
                        });
                    }
                }
            }

            return View(vm);
        }

        // ── 2. PAGE "SUIVI QUOTIDIEN" ──────────────────────────────────────────
        public ActionResult Quotidien(DateTime? date)
        {
            var vm = new QuotidienViewModel();

            using (var connexion = new SqlConnection(ConnexionString))
            {
                connexion.Open();

                using (var cmdBornes = new SqlCommand("SELECT MIN(Date), MAX(Date) FROM MonitoringQuotidien", connexion))
                using (var reader = cmdBornes.ExecuteReader())
                {
                    reader.Read();
                    vm.DateMin = reader.GetDateTime(0);
                    vm.DateMax = reader.GetDateTime(1);
                }

                vm.DateAffichee = date ?? vm.DateMax;

                string requeteJour = @"
                    SELECT Date, SubjectName, NombreReclamations, MoyenneMobile, ZScore, EstAnomalie
                    FROM MonitoringQuotidien
                    WHERE Date = @date
                    ORDER BY EstAnomalie DESC, NombreReclamations DESC";

                using (var cmd = new SqlCommand(requeteJour, connexion))
                {
                    cmd.Parameters.AddWithValue("@date", vm.DateAffichee);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            vm.Resultats.Add(new MonitoringQuotidien
                            {
                                Date               = reader.GetDateTime(0),
                                SubjectName        = reader.GetString(1),
                                NombreReclamations = reader.GetInt64(2),
                                MoyenneMobile      = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3),
                                ZScore             = reader.IsDBNull(4) ? (double?)null : reader.GetDouble(4),
                                EstAnomalie        = reader.GetBoolean(5)
                            });
                        }
                    }
                }
            }

            return View(vm);
        }

        // ── 3. PAGE "ANALYSE DES SUJETS" ──────────────────────────────────────
        public ActionResult Sujets(string subjectName, DateTime? date)
        {
            var vm = new SujetsViewModel();

            // Sujets clefs prédéfinis
            vm.SujetsClefs = new List<SubjectKeyInfo>
            {
                new SubjectKeyInfo { Code = "RET632", Libelle = "Échec de connexion", Description = "Problèmes d'authentification et de connexion au réseau" },
                new SubjectKeyInfo { Code = "REC541", Libelle = "Perte de solde", Description = "Analyses des prélèvements, Service Plus, SOS Crédit et Data" },
                new SubjectKeyInfo { Code = "REC531", Libelle = "Échec de recharge", Description = "Cartes grattées, D17, Maxit, paiements bancaires et USSD" },
                new SubjectKeyInfo { Code = "INF261", Libelle = "Offre Mobile", Description = "Renseignements sur les types d'offres et formules souscrites" }
            };

            // Sujet par défaut si non spécifié
            vm.SubjectSelected = string.IsNullOrEmpty(subjectName) ? "RET632" : subjectName;

            using (var connexion = new SqlConnection(ConnexionString))
            {
                connexion.Open();

                using (var cmdBornes = new SqlCommand("SELECT MIN(Date), MAX(Date) FROM MonitoringQuotidien", connexion))
                using (var reader = cmdBornes.ExecuteReader())
                {
                    reader.Read();
                    vm.DateMin = reader.GetDateTime(0);
                    vm.DateMax = reader.GetDateTime(1);
                }

                // Trouver la date appropriée (date demandée ou dernière date avec des détails)
                if (date.HasValue)
                {
                    vm.DateSelected = date.Value;
                }
                else
                {
                    string reqLastDate = @"
                        SELECT TOP 1 Date FROM DetailAnomalie 
                        WHERE SubjectName LIKE @sujet + '%' 
                        ORDER BY Date DESC";
                    using (var cmdDate = new SqlCommand(reqLastDate, connexion))
                    {
                        cmdDate.Parameters.AddWithValue("@sujet", vm.SubjectSelected);
                        var objDate = cmdDate.ExecuteScalar();
                        if (objDate != null && objDate != DBNull.Value)
                            vm.DateSelected = Convert.ToDateTime(objDate);
                        else
                            vm.DateSelected = vm.DateMax;
                    }
                }

                // ── KPIs du sujet sur les 30 derniers jours ─────────────────────────────
                DateTime dateDebut30j = vm.DateMax.AddDays(-29);
                string requeteKpiSujet = @"
                    SELECT 
                        ISNULL(SUM(NombreReclamations), 0),
                        ISNULL(SUM(CASE WHEN EstAnomalie = 1 THEN 1 ELSE 0 END), 0),
                        COUNT(*)
                    FROM MonitoringQuotidien
                    WHERE SubjectName LIKE @sujet + '%' AND Date >= @debut";
                using (var cmdKpi = new SqlCommand(requeteKpiSujet, connexion))
                {
                    cmdKpi.Parameters.AddWithValue("@sujet", vm.SubjectSelected);
                    cmdKpi.Parameters.AddWithValue("@debut", dateDebut30j);
                    using (var reader = cmdKpi.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            vm.TotalReclamations30j = Convert.ToInt64(reader.GetValue(0));
                            vm.NbJoursAnomalie30j   = Convert.ToInt32(reader.GetValue(1));
                            int totalJrs            = Convert.ToInt32(reader.GetValue(2));
                            vm.TauxAnomalie30j      = totalJrs > 0 ? Math.Round((double)vm.NbJoursAnomalie30j / totalJrs * 100.0, 1) : 0;
                        }
                    }
                }

                // Statistique du jour pour le sujet
                string requeteStat = @"
                    SELECT Date, SubjectName, NombreReclamations, MoyenneMobile, ZScore, EstAnomalie
                    FROM MonitoringQuotidien
                    WHERE SubjectName LIKE @sujet + '%' AND Date = @date";
                using (var cmdStat = new SqlCommand(requeteStat, connexion))
                {
                    cmdStat.Parameters.AddWithValue("@sujet", vm.SubjectSelected);
                    cmdStat.Parameters.AddWithValue("@date", vm.DateSelected);
                    using (var reader = cmdStat.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            vm.StatJour = new MonitoringQuotidien
                            {
                                Date               = reader.GetDateTime(0),
                                SubjectName        = reader.GetString(1),
                                NombreReclamations = reader.GetInt64(2),
                                MoyenneMobile      = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3),
                                ZScore             = reader.IsDBNull(4) ? (double?)null : reader.GetDouble(4),
                                EstAnomalie        = reader.GetBoolean(5)
                            };
                        }
                    }
                }

                // Détails de répartition pour la date sélectionnée
                string requeteDetail = @"
                    SELECT Date, SubjectName, Categorie, Valeur, Nombre
                    FROM DetailAnomalie
                    WHERE SubjectName LIKE @sujet + '%' AND Date = @date";

                using (var cmd = new SqlCommand(requeteDetail, connexion))
                {
                    cmd.Parameters.AddWithValue("@sujet", vm.SubjectSelected);
                    cmd.Parameters.AddWithValue("@date", vm.DateSelected);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            vm.Details.Add(new DetailAnomalie
                            {
                                Date        = reader.GetDateTime(0),
                                SubjectName = reader.GetString(1),
                                Categorie   = reader.GetString(2),
                                Valeur      = reader.GetString(3),
                                Nombre      = reader.GetInt64(4)
                            });
                        }
                    }
                }

                // ── Si aucun détail pour cette date spécifique, charger la dernière répartition connue ──
                if (vm.Details.Count == 0)
                {
                    string reqLastDetails = @"
                        SELECT Date, SubjectName, Categorie, Valeur, Nombre
                        FROM DetailAnomalie
                        WHERE SubjectName LIKE @sujet + '%' AND Date = (
                            SELECT MAX(Date) FROM DetailAnomalie WHERE SubjectName LIKE @sujet + '%'
                        )";
                    using (var cmdLast = new SqlCommand(reqLastDetails, connexion))
                    {
                        cmdLast.Parameters.AddWithValue("@sujet", vm.SubjectSelected);
                        using (var reader = cmdLast.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                vm.Details.Add(new DetailAnomalie
                                {
                                    Date        = reader.GetDateTime(0),
                                    SubjectName = reader.GetString(1),
                                    Categorie   = reader.GetString(2),
                                    Valeur      = reader.GetString(3),
                                    Nombre      = reader.GetInt64(4)
                                });
                            }
                        }
                    }
                }
            }

            // Groupement structuré des catégories
            var categoriesGrouped = vm.Details
                .GroupBy(d => d.Categorie)
                .Select(g => new
                {
                    Categorie = g.Key,
                    TotalCat  = g.Sum(d => d.Nombre),
                    Items     = g.OrderByDescending(d => d.Nombre)
                                 .Select(d => new { d.Valeur, d.Nombre })
                                 .ToList()
                })
                .ToList();

            foreach (var cat in categoriesGrouped)
            {
                var group = new CategoryDetailGroup
                {
                    Categorie      = cat.Categorie,
                    Libelle        = LabeliserCategorie(cat.Categorie),
                    TotalCategorie = cat.TotalCat
                };

                foreach (var item in cat.Items)
                {
                    group.Items.Add(new CategoryValueItem
                    {
                        Valeur      = item.Valeur,
                        Total       = item.Nombre,
                        Pourcentage = cat.TotalCat > 0 ? Math.Round((double)item.Nombre / cat.TotalCat * 100.0, 1) : 0
                    });
                }

                if (group.Items.Count > 0)
                {
                    group.TopValeur      = group.Items[0].Valeur;
                    group.TopPourcentage = group.Items[0].Pourcentage;
                }

                vm.CategoriesData.Add(group);
            }

            // JSON pour Chart.js
            var categoriesForJson = categoriesGrouped.Select(cg => new
            {
                Categorie   = cg.Categorie,
                Libelle     = LabeliserCategorie(cg.Categorie),
                Repartition = cg.Items.ToDictionary(i => i.Valeur, i => i.Nombre)
            }).ToList();

            vm.CategoriesJson   = JsonConvert.SerializeObject(categoriesForJson);
            vm.DetailDisponible = vm.CategoriesData.Count > 0;

            // Génération dynamique des insights intelligents selon le sujet
            GenererInsightsIntelligents(vm);

            return View(vm);
        }

        private static string LabeliserCategorie(string catCode)
        {
            switch (catCode?.ToLower())
            {
                case "produit": return "Produit Concerné";
                case "technologie": return "Technologie Réseau";
                case "cause": return "Cause Principale";
                case "cause_perte": return "Cause de Perte de Solde";
                case "cause_probable": return "Cause Probable de l'Échec";
                case "canal": return "Canal de Recharge";
                case "type_offre": return "Type d'Offre Mobile";
                default:
                    return catCode?.Replace('_', ' ');
            }
        }

        private static void GenererInsightsIntelligents(SujetsViewModel vm)
        {
            if (vm.CategoriesData == null || vm.CategoriesData.Count == 0)
            {
                vm.MainInsight = "Données analytiques indisponibles pour ce sujet.";
                vm.SubInsight  = "Les catégories s'afficheront dès qu'une classification sera enregistrée.";
                return;
            }

            if (vm.SubjectSelected.StartsWith("RET632", StringComparison.OrdinalIgnoreCase))
            {
                var prodGroup = vm.CategoriesData.FirstOrDefault(c => c.Categorie.Equals("produit", StringComparison.OrdinalIgnoreCase));
                var techGroup = vm.CategoriesData.FirstOrDefault(c => c.Categorie.Equals("technologie", StringComparison.OrdinalIgnoreCase));
                var causeGroup = vm.CategoriesData.FirstOrDefault(c => c.Categorie.Equals("cause", StringComparison.OrdinalIgnoreCase));

                string prodLead = prodGroup != null && !string.IsNullOrEmpty(prodGroup.TopValeur) ? $"{prodGroup.TopValeur} ({prodGroup.TopPourcentage}%)" : "Non spécifié";
                string techLead = techGroup != null && !string.IsNullOrEmpty(techGroup.TopValeur) ? $"{techGroup.TopValeur} ({techGroup.TopPourcentage}%)" : "Non spécifié";

                vm.TopFactorLabel = "Produit le plus impacté";
                vm.TopFactorValue = prodLead;
                vm.MainInsight    = $"Le produit {prodLead} est la principale source de réclamations d'échec de connexion.";
                vm.SubInsight     = $"Sur le réseau, la technologie {techLead} enregistre la plus forte concentration d'incidents.";
            }
            else if (vm.SubjectSelected.StartsWith("REC541", StringComparison.OrdinalIgnoreCase))
            {
                var causeGroup = vm.CategoriesData.FirstOrDefault(c => c.Categorie.Equals("cause_perte", StringComparison.OrdinalIgnoreCase));
                string mainCause = causeGroup != null && !string.IsNullOrEmpty(causeGroup.TopValeur) ? $"{causeGroup.TopValeur} ({causeGroup.TopPourcentage}%)" : "Non identifiée";

                vm.TopFactorLabel = "Cause majeure de perte";
                vm.TopFactorValue = mainCause;
                vm.MainInsight    = $"La cause majeure de perte de solde est {mainCause}.";
                vm.SubInsight     = "Cette catégorie représente la majorité des sollicitations clients pour contestation de solde.";
            }
            else if (vm.SubjectSelected.StartsWith("REC531", StringComparison.OrdinalIgnoreCase))
            {
                var canalGroup = vm.CategoriesData.FirstOrDefault(c => c.Categorie.Equals("canal", StringComparison.OrdinalIgnoreCase));
                var causeGroup = vm.CategoriesData.FirstOrDefault(c => c.Categorie.Equals("cause_probable", StringComparison.OrdinalIgnoreCase));

                string canalLead = canalGroup != null && !string.IsNullOrEmpty(canalGroup.TopValeur) ? $"{canalGroup.TopValeur} ({canalGroup.TopPourcentage}%)" : "Non spécifié";
                string causeLead = causeGroup != null && !string.IsNullOrEmpty(causeGroup.TopValeur) ? $"{causeGroup.TopValeur} ({causeGroup.TopPourcentage}%)" : "Non spécifiée";

                vm.TopFactorLabel = "Canal le plus concerné";
                vm.TopFactorValue = canalLead;
                vm.MainInsight    = $"Le canal de recharge {canalLead} enregistre le plus fort taux d'échec.";
                vm.SubInsight     = $"Motif principal d'échec identifié : {causeLead}.";
            }
            else if (vm.SubjectSelected.StartsWith("INF261", StringComparison.OrdinalIgnoreCase))
            {
                var offreGroup = vm.CategoriesData.FirstOrDefault(c => c.Categorie.Equals("type_offre", StringComparison.OrdinalIgnoreCase));
                string offreLead = offreGroup != null && !string.IsNullOrEmpty(offreGroup.TopValeur) ? $"{offreGroup.TopValeur} ({offreGroup.TopPourcentage}%)" : "Prépayée";

                vm.TopFactorLabel = "Offre la plus demandée";
                vm.TopFactorValue = offreLead;
                vm.MainInsight    = $"La formule {offreLead} concentre la majorité des demandes d'information.";
                vm.SubInsight     = "Suivi régulier du volume d'informations demandées sur cette gamme.";
            }
        }

        // Redirection legacy pour assurer la compatibilité
        public ActionResult Detail(string subjectName, DateTime? date)
        {
            return RedirectToAction("Sujets", new { subjectName = subjectName, date = date?.ToString("yyyy-MM-dd") });
        }
    }
}

        
