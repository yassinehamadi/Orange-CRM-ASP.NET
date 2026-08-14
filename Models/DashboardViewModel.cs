using System;
using System.Collections.Generic;

namespace IHM_Monitoring.Models
{
    /// <summary>
    /// ViewModel principal du dashboard CRM.
    /// Regroupe les KPIs, graphiques, classements et tableau quotidien.
    /// </summary>
    public class DashboardViewModel
    {
        // ── Navigation ─────────────────────────────────────────────
        public DateTime DateAffichee { get; set; }
        public DateTime DateMin { get; set; }
        public DateTime DateMax { get; set; }

        // ── KPI Cards (30 derniers jours depuis DateMax) ────────────
        public long TotalReclamations { get; set; }
        public long TotalAnomalies { get; set; }
        public string SujetCritique { get; set; }

        // ── Évolution (7 j courants vs 7 j précédents) ─────────────
        public double Evolution { get; set; }
        public bool EvolutionPositive { get; set; }
        public bool EvolutionDisponible { get; set; }

        // ── Graphique timeline — sérialisé en JSON pour Chart.js ───
        public string EvolutionTimelineJson { get; set; }

        // ── Classements (toute la période disponible) ───────────────
        public List<TopSujetItem> TopSujets { get; set; } = new List<TopSujetItem>();
        public List<TopAnomalieItem> TopAnomalies { get; set; } = new List<TopAnomalieItem>();
        public List<TauxAnomalieItem> TableauTaux { get; set; } = new List<TauxAnomalieItem>();

        // ── Tableau quotidien existant ──────────────────────────────
        public List<MonitoringQuotidien> TableauQuotidien { get; set; } = new List<MonitoringQuotidien>();
    }

    /// <summary>Ligne du classement par volume de réclamations.</summary>
    public class TopSujetItem
    {
        public string SubjectName { get; set; }
        public long TotalReclamations { get; set; }
        /// <summary>Pourcentage du total général (0–100).</summary>
        public double Pourcentage { get; set; }
    }

    /// <summary>Ligne du classement par nombre de jours en anomalie.</summary>
    public class TopAnomalieItem
    {
        public string SubjectName { get; set; }
        public int NbJoursAnomalie { get; set; }
        /// <summary>Largeur de barre relative au maximum (0–100).</summary>
        public double PourcentageBar { get; set; }
    }

    /// <summary>Ligne du tableau taux d'anomalie.</summary>
    public class TauxAnomalieItem
    {
        public string SubjectName { get; set; }
        public long TotalReclamations { get; set; }
        public int NbJoursAnomalie { get; set; }
        public int TotalJours { get; set; }
        /// <summary>Taux = NbJoursAnomalie / TotalJours × 100.</summary>
        public double TauxAnomalie { get; set; }
    }

    /// <summary>ViewModel pour la page Suivi Quotidien.</summary>
    public class QuotidienViewModel
    {
        public DateTime DateAffichee { get; set; }
        public DateTime DateMin { get; set; }
        public DateTime DateMax { get; set; }
        public List<MonitoringQuotidien> Resultats { get; set; } = new List<MonitoringQuotidien>();
    }

    /// <summary>Information sur un sujet clé pour la navigation.</summary>
    public class SubjectKeyInfo
    {
        public string Code { get; set; }
        public string Libelle { get; set; }
        public string Description { get; set; }
    }

    /// <summary>Information sur une valeur au sein d'une catégorie.</summary>
    public class CategoryValueItem
    {
        public string Valeur { get; set; }
        public long Total { get; set; }
        public double Pourcentage { get; set; }
    }

    /// <summary>Groupe de détails par catégorie d'anomalie.</summary>
    public class CategoryDetailGroup
    {
        public string Categorie { get; set; }
        public string Libelle { get; set; }
        public long TotalCategorie { get; set; }
        public string TopValeur { get; set; }
        public double TopPourcentage { get; set; }
        public List<CategoryValueItem> Items { get; set; } = new List<CategoryValueItem>();
    }

    /// <summary>ViewModel pour la page Analyse des Sujets.</summary>
    public class SujetsViewModel
    {
        public string SubjectSelected { get; set; }
        public DateTime DateSelected { get; set; }
        public DateTime DateMin { get; set; }
        public DateTime DateMax { get; set; }
        
        // ── KPIs Globaux du Sujet sur 30 jours ────────────────────
        public long TotalReclamations30j { get; set; }
        public int NbJoursAnomalie30j { get; set; }
        public double TauxAnomalie30j { get; set; }

        // ── Insights Intelligents Automatiques ─────────────────────
        public string MainInsight { get; set; }
        public string SubInsight { get; set; }
        public string TopFactorLabel { get; set; }
        public string TopFactorValue { get; set; }

        // ── Données par Catégorie ──────────────────────────────────
        public MonitoringQuotidien StatJour { get; set; }
        public List<DetailAnomalie> Details { get; set; } = new List<DetailAnomalie>();
        public List<CategoryDetailGroup> CategoriesData { get; set; } = new List<CategoryDetailGroup>();
        public string CategoriesJson { get; set; }
        public string TimelineJson { get; set; }
        public bool DetailDisponible { get; set; }
        public List<SubjectKeyInfo> SujetsClefs { get; set; } = new List<SubjectKeyInfo>();
    }
}
