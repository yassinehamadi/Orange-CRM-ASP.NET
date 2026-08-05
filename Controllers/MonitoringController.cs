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
            var resultats = new List<MonitoringQuotidien>();
            DateTime dateAffichee, dateMin, dateMax;

            using (var connexion = new SqlConnection(ConnexionString))
            {
                connexion.Open();

                using (var cmdBornes = new SqlCommand("SELECT MIN(Date), MAX(Date) FROM MonitoringQuotidien", connexion))
                using (var reader = cmdBornes.ExecuteReader())
                {
                    reader.Read();
                    dateMin = reader.GetDateTime(0);
                    dateMax = reader.GetDateTime(1);
                }

                dateAffichee = date ?? dateMax;

                string requete = @"
                    SELECT Date, SubjectName, NombreReclamations, MoyenneMobile, ZScore, EstAnomalie
                    FROM MonitoringQuotidien
                    WHERE Date = @date
                    ORDER BY EstAnomalie DESC, NombreReclamations DESC";

                using (var cmd = new SqlCommand(requete, connexion))
                {
                    cmd.Parameters.AddWithValue("@date", dateAffichee);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            resultats.Add(new MonitoringQuotidien
                            {
                                Date = reader.GetDateTime(0),
                                SubjectName = reader.GetString(1),
                                NombreReclamations = reader.GetInt64(2),
                                MoyenneMobile = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3),
                                ZScore = reader.IsDBNull(4) ? (double?)null : reader.GetDouble(4),
                                EstAnomalie = reader.GetBoolean(5)
                            });
                        }
                    }
                }
            }

            ViewBag.DateAffichee = dateAffichee;
            ViewBag.DateMin = dateMin;
            ViewBag.DateMax = dateMax;

            return View(resultats);
        }

        public ActionResult Detail(string subjectName, DateTime? date)
        {
            // Si les paramètres sont manquants ou invalides, on renvoie proprement vers Index
            if (string.IsNullOrEmpty(subjectName) || !date.HasValue)
            {
                return RedirectToAction("Index");
            }

            DateTime dateValeur = date.Value;

            var details = new List<DetailAnomalie>();
            MonitoringQuotidien statJour = null;

            using (var connexion = new SqlConnection(ConnexionString))
            {
                connexion.Open();

                string requeteStat = @"
            SELECT Date, SubjectName, NombreReclamations, MoyenneMobile, ZScore, EstAnomalie
            FROM MonitoringQuotidien
            WHERE SubjectName = @sujet AND Date = @date";
                using (var cmdStat = new SqlCommand(requeteStat, connexion))
                {
                    cmdStat.Parameters.AddWithValue("@sujet", subjectName);
                    cmdStat.Parameters.AddWithValue("@date", dateValeur);
                    using (var reader = cmdStat.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            statJour = new MonitoringQuotidien
                            {
                                NombreReclamations = reader.GetInt64(2),
                                MoyenneMobile = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3),
                                ZScore = reader.IsDBNull(4) ? (double?)null : reader.GetDouble(4)
                            };
                        }
                    }
                }

                string requete = @"
            SELECT Date, SubjectName, Categorie, Valeur, Nombre
            FROM DetailAnomalie
            WHERE SubjectName = @sujet AND Date = @date";

                using (var cmd = new SqlCommand(requete, connexion))
                {
                    cmd.Parameters.AddWithValue("@sujet", subjectName);
                    cmd.Parameters.AddWithValue("@date", dateValeur);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            details.Add(new DetailAnomalie
                            {
                                Categorie = reader.GetString(2),
                                Valeur = reader.GetString(3),
                                Nombre = reader.GetInt64(4)
                            });
                        }
                    }
                }
            }

            ViewBag.JsonProduit = JsonConvert.SerializeObject(
                details.Where(d => d.Categorie == "produit").OrderByDescending(d => d.Nombre)
                       .ToDictionary(d => d.Valeur, d => d.Nombre));
            ViewBag.JsonTechnologie = JsonConvert.SerializeObject(
                details.Where(d => d.Categorie == "technologie").OrderByDescending(d => d.Nombre)
                       .ToDictionary(d => d.Valeur, d => d.Nombre));
            ViewBag.JsonCause = JsonConvert.SerializeObject(
                details.Where(d => d.Categorie == "cause").OrderByDescending(d => d.Nombre)
                       .ToDictionary(d => d.Valeur, d => d.Nombre));

            ViewBag.SubjectName = subjectName;
            ViewBag.Date = dateValeur;
            ViewBag.DetailDisponible = details.Count > 0;
            ViewBag.StatJour = statJour;

            return View(details);
        }
    }
}

        
