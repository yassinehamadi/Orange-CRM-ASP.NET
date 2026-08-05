using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace IHM_Monitoring.Models
{
    public class DetailAnomalie
    {
        public DateTime Date { get; set; }
        public string SubjectName { get; set; }
        public string Categorie { get; set; }
        public string Valeur { get; set; }
        public long Nombre { get; set; }
    }
}