using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace IHM_Monitoring.Models
{
    public class MonitoringQuotidien
    {
        public DateTime Date { get; set; }
        public string SubjectName { get; set; }
        public long NombreReclamations { get; set; }
        public double? MoyenneMobile { get; set; }
        public double? ZScore { get; set; }
        public bool EstAnomalie { get; set; }
    }
}