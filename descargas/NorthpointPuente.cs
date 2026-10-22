// NORTHPOINT · Puente para NinjaTrader 8
// Manda tus ejecuciones y posiciones a la app NORTHPOINT (Windows) en automático.
// Instalación: copia este archivo a  Documentos\NinjaTrader 8\bin\Custom\AddOns\
// y en NinjaTrader abre New → NinjaScript Editor y presiona F5 (compilar). Listo: queda activo siempre que NinjaTrader esté abierto.
// Escribe en  Documentos\NinjaTrader 8\northpoint\northpoint_nt.json  (solo lectura de tus datos; no envía órdenes).
#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
    public class NorthpointPuente : AddOnBase
    {
        private readonly object lk = new object();
        private readonly Dictionary<string, string> ejec = new Dictionary<string, string>();
        private readonly HashSet<string> enganchadas = new HashSet<string>();
        private string carpeta, archivo, historial;
        private Timer reloj;
        private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NorthpointPuente";
                Description = "Manda tus trades a NORTHPOINT en automático";
            }
            else if (State == State.Active)
            {
                carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "northpoint");
                Directory.CreateDirectory(carpeta);
                archivo = Path.Combine(carpeta, "northpoint_nt.json");
                historial = Path.Combine(carpeta, "ejecuciones.jsonl");
                Cargar();
                reloj = new Timer(_ => { try { EngancharTodas(); Escribir(); } catch { } }, null, 0, 3000);
            }
            else if (State == State.Terminated)
            {
                if (reloj != null) reloj.Dispose();
                lock (Account.All)
                    foreach (Account a in Account.All)
                        if (enganchadas.Contains(a.Name)) { a.ExecutionUpdate -= OnEjecucion; }
            }
        }

        private void EngancharTodas()
        {
            lock (Account.All)
                foreach (Account a in Account.All)
                {
                    if (enganchadas.Contains(a.Name)) continue;
                    enganchadas.Add(a.Name);
                    a.ExecutionUpdate += OnEjecucion;
                    lock (a.Executions) foreach (Execution x in a.Executions) Agregar(x);
                }
        }

        private void OnEjecucion(object sender, ExecutionEventArgs e)
        {
            if (e.Execution == null) return;
            Agregar(e.Execution);
            try { Escribir(); } catch { }
        }

        private static string J(string s) { return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
        private static string N(double d) { return double.IsNaN(d) || double.IsInfinity(d) ? "0" : d.ToString("0.########", IC); }
        private static long T(DateTime t) { return (long)(t.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

        private void Agregar(Execution x)
        {
            if (x == null || x.Instrument == null || x.Account == null) return;
            string id = x.Account.Name + ":" + x.ExecutionId;
            string linea = "{\"id\":" + J(id) + ",\"cuenta\":" + J(x.Account.Name) + ",\"inst\":" + J(x.Instrument.FullName) +
                ",\"lado\":" + J(x.MarketPosition == MarketPosition.Long ? "B" : "S") + ",\"qty\":" + x.Quantity.ToString(IC) +
                ",\"precio\":" + N(x.Price) + ",\"pv\":" + N(x.Instrument.MasterInstrument.PointValue) +
                ",\"com\":" + N(x.Commission) + ",\"t\":" + T(x.Time).ToString(IC) + "}";
            lock (lk)
            {
                if (ejec.ContainsKey(id)) return;
                ejec[id] = linea;
                try { File.AppendAllText(historial, linea + Environment.NewLine); } catch { }
            }
        }

        private void Cargar()
        {
            try
            {
                if (!File.Exists(historial)) return;
                foreach (string l in File.ReadAllLines(historial))
                {
                    int i = l.IndexOf("\"id\":\"", StringComparison.Ordinal); if (i < 0) continue;
                    int a = i + 6, b = l.IndexOf('"', a); if (b < 0) continue;
                    ejec[l.Substring(a, b - a)] = l;
                }
            }
            catch { }
        }

        private void Escribir()
        {
            var sb = new StringBuilder();
            sb.Append("{\"v\":1,\"t\":").Append(T(DateTime.Now).ToString(IC)).Append(",\"cuentas\":[");
            var pos = new List<string>(); bool primera = true;
            lock (Account.All)
                foreach (Account a in Account.All)
                {
                    double bal = 0; try { bal = a.Get(AccountItem.CashValue, Currency.UsDollar); } catch { }
                    if (!primera) sb.Append(','); primera = false;
                    sb.Append("{\"nombre\":").Append(J(a.Name)).Append(",\"balance\":").Append(N(bal)).Append('}');
                    lock (a.Positions)
                        foreach (Position p in a.Positions)
                        {
                            if (p.MarketPosition == MarketPosition.Flat || p.Instrument == null) continue;
                            double upl = 0; try { upl = p.GetUnrealizedProfitLoss(PerformanceUnit.Currency); } catch { }
                            pos.Add("{\"cuenta\":" + J(a.Name) + ",\"inst\":" + J(p.Instrument.FullName) + ",\"dir\":" + J(p.MarketPosition == MarketPosition.Long ? "L" : "S") +
                                ",\"qty\":" + p.Quantity.ToString(IC) + ",\"precio\":" + N(p.AveragePrice) + ",\"flot\":" + N(upl) + "}");
                        }
                }
            sb.Append("],\"posiciones\":[").Append(string.Join(",", pos)).Append("],\"ejecuciones\":[");
            lock (lk) sb.Append(string.Join(",", ejec.Values.Skip(Math.Max(0, ejec.Count - 2000))));
            sb.Append("]}");
            string tmp = archivo + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            File.Copy(tmp, archivo, true);
        }
    }
}
