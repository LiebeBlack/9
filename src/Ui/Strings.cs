// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - textos en espanol e ingles.
//
// Se implementa como tabla en codigo y no como .resx porque el proyecto se compila con csc
// directo, sin resgen: menos piezas, mismo resultado. El idioma se elige por la cultura del
// sistema y siempre cae a ingles si no hay traduccion.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace NetForge.Ui
{
    public static class Strings
    {
        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>(StringComparer.Ordinal);

        static Strings()
        {
            // clave, espanol, ingles
            Add("app.title", "NetForge Studio", "NetForge Studio");
            Add("app.subtitle", "Router local y transferencia de archivos, sin internet", "Local router and file transfer, no internet needed");
            Add("tab.clients", "Clientes", "Clients");
            Add("tab.parole", "Indultos", "Pardons");
            Add("tab.files", "Archivos", "Files");
            Add("tab.diagnostics", "Diagnostico", "Diagnostics");
            Add("tab.log", "Registro", "Log");
            Add("network.ssid", "Nombre de la red (SSID)", "Network name (SSID)");
            Add("network.password", "Clave", "Password");
            Add("network.show", "Mostrar", "Show");
            Add("network.create", "Crear red", "Start network");
            Add("network.stop", "Detener red", "Stop network");
            Add("network.share", "Compartir internet", "Share internet");
            Add("network.engine", "Motor", "Engine");
            Add("network.engine.auto", "Automatico", "Automatic");
            Add("network.engine.offgrid", "Off-grid (Wi-Fi Direct)", "Off-grid (Wi-Fi Direct)");
            Add("network.engine.sharing", "Compartir (punto de acceso)", "Sharing (hotspot)");
            Add("network.engine.legacy", "Red hospedada (legacy)", "Hosted network (legacy)");
            Add("network.state.idle", "Sin red", "No network");
            Add("network.state.starting", "Levantando...", "Starting...");
            Add("network.state.on", "Activa", "Active");
            Add("network.state.stopping", "Deteniendo...", "Stopping...");
            Add("network.state.faulted", "Caida (recuperando)", "Down (recovering)");
            Add("network.guard", "Auto-resurreccion", "Auto-recovery");
            Add("network.clients", "dispositivos", "devices");
            Add("clients.ip", "IP", "IP");
            Add("clients.mac", "MAC", "MAC");
            Add("clients.vendor", "Fabricante", "Vendor");
            Add("clients.host", "Nombre", "Hostname");
            Add("clients.state", "Estado", "State");
            Add("clients.source", "Origen", "Source");
            Add("clients.hammer", "Banear (El Martillo)", "Ban (The Hammer)");
            Add("clients.export", "Exportar CSV", "Export CSV");
            Add("clients.empty", "Todavia no hay dispositivos conectados.", "No devices connected yet.");
            Add("clients.ask", "Seguro que quieres bloquear a", "Block this device?");
            Add("parole.mac", "MAC", "MAC");
            Add("parole.ip", "Ultima IP", "Last IP");
            Add("parole.when", "Cuando", "When");
            Add("parole.reason", "Motivo", "Reason");
            Add("parole.vendor", "Fabricante", "Vendor");
            Add("parole.pardon", "Indultar", "Pardon");
            Add("parole.pardonall", "Indultar a todos", "Pardon everyone");
            Add("parole.empty", "No hay ningun dispositivo bloqueado.", "No blocked devices.");
            Add("files.folder", "Carpeta a compartir", "Folder to share");
            Add("files.browse", "Elegir carpeta", "Choose folder");
            Add("files.start", "Iniciar servidores", "Start servers");
            Add("files.stop", "Detener servidores", "Stop servers");
            Add("files.allowupload", "Permitir que los moviles suban archivos", "Allow phones to upload files");
            Add("files.allowdelete", "Permitir borrar (peligroso)", "Allow deleting (dangerous)");
            Add("files.portal", "Portal web (recomendado)", "Web portal (recommended)");
            Add("files.ftp", "Servidor FTP", "FTP server");
            Add("files.user", "Usuario", "User");
            Add("files.qr", "Escanea para abrir en el movil", "Scan to open on your phone");
            Add("files.nored", "Primero hay que crear la red local.", "Start the local network first.");
            Add("files.sent", "Enviado", "Sent");
            Add("files.received", "Recibido", "Received");
            Add("files.copy", "Copiar enlace", "Copy link");
            Add("diag.refresh", "Volver a analizar", "Re-analyze");
            Add("diag.copy", "Copiar informe", "Copy report");
            Add("log.dump", "Guardar registro", "Save log");
            Add("log.open", "Abrir carpeta", "Open folder");
            Add("tray.show", "Mostrar panel", "Show panel");
            Add("tray.files", "Detener archivos", "Stop file servers");
            Add("tray.exit", "Salir del todo", "Exit completely");
            Add("tray.portal", "Abrir el portal web", "Open the web portal");
            Add("tray.copyportal", "Copiar la direccion del portal", "Copy the portal address");
            Add("tray.joined", "Dispositivo nuevo en la red", "New device on the network");
            Add("tray.ghost", "Modo Fantasma activo", "Ghost mode active");
            Add("tray.tip", "NetForge Studio - red local activa", "NetForge Studio - local network active");
            Add("banner.noelevated", "Sin permisos de administrador: algunas funciones fallaran", "No administrator rights: some features will fail");
            Add("error.radiooff", "La radio Wi-Fi esta apagada. Enciende el Wi-Fi (o quita el modo avion) e intentalo otra vez.", "The Wi-Fi radio is off. Turn Wi-Fi on (or leave airplane mode) and try again.");
            Add("error.nonetwork", "Todavia no hay red local levantada.", "There is no local network running yet.");
            Add("error.noselection", "No hay ningun dispositivo seleccionado.", "No device is selected.");
            Add("error.noapaddress", "todavia no hay direccion de red local", "there is no local network address yet");
            Add("error.nofolder", "la carpeta a compartir no existe", "the folder to share does not exist");
            Add("error.servers", "portal: {0} / ftp: {1}", "portal: {0} / ftp: {1}");
            Add("error.serverpartial", "el portal web no arranco ({0}); el FTP sigue disponible", "the web portal did not start ({0}); FTP is still available");
            Add("tray.networkup", "Red \"{0}\" activa en {1}", "Network \"{0}\" active at {1}");
            Add("tray.ghostmessage", "Modo Fantasma: la red sigue activa. Doble clic en el icono para volver.", "Ghost mode: the network is still running. Double-click the tray icon to come back.");
            Add("autostart.minimized", "Arrancar minimizado en la bandeja", "Start minimized in the tray");
            Add("files.off", "(apagado)", "(off)");
            Add("files.noqr", "no se pudo generar el codigo", "the QR code could not be generated");
            Add("settings.tab", "Ajustes", "Settings");
            Add("settings.language", "Idioma de la interfaz", "Interface language");
            Add("settings.language.auto", "Automatico (segun Windows)", "Automatic (follow Windows)");
            Add("settings.band", "Banda preferida del punto de acceso", "Preferred access point band");
            Add("settings.band.2", "2,4 GHz (mas alcance)", "2.4 GHz (longer range)");
            Add("settings.band.5", "5 GHz (mas velocidad)", "5 GHz (faster)");
            Add("settings.radar", "Auditoria del radar (segundos)", "Radar sweep (seconds)");
            Add("settings.guard", "Vigilancia del Daemon Guard (segundos)", "Daemon Guard polling (seconds)");
            Add("settings.portalport", "Puerto del portal web", "Web portal port");
            Add("settings.ftpport", "Puerto del servidor FTP", "FTP server port");
            Add("settings.save", "Guardar y aplicar", "Save and apply");
            Add("settings.defaults", "Valores por defecto", "Restore defaults");
            Add("settings.openfolder", "Abrir carpeta de datos", "Open data folder");
            Add("settings.applied", "Ajustes guardados y aplicados.", "Settings saved and applied.");
            Add("settings.restartnetwork", "Los intervalos y la banda se aplican al crear la red; los puertos, al iniciar los servidores de archivos.", "Intervals and band apply when the network is created; ports apply when the file servers start.");
            Add("settings.invalidport", "El puerto debe estar entre 1024 y 65535.", "The port must be between 1024 and 65535.");
            Add("settings.section.network", "Red", "Network");
            Add("settings.section.service", "Servicio de Windows", "Windows service");
            Add("settings.service.state", "Servicio de uso compartido (SharedAccess)", "Sharing service (SharedAccess)");
            Add("settings.service.running", "en ejecucion", "running");
            Add("settings.service.stopped", "detenido", "stopped");
            Add("settings.service.starting", "arrancando", "starting");
            Add("settings.service.other", "en otro estado", "in another state");
            Add("settings.service.missing", "no existe en este Windows", "not present on this Windows");
            Add("settings.service.start", "Arrancar el servicio", "Start the service");
            Add("settings.service.started", "Servicio arrancado y puesto en arranque automatico.", "Service started and set to automatic startup.");
            Add("settings.service.why", "Es el unico servicio de Windows que usa NetForge, y solo para repartir internet. La red local no depende de el: se crea con el propio adaptador Wi-Fi.", "It is the only Windows service NetForge uses, and only to share internet. The local network does not depend on it: it is created with the Wi-Fi adapter itself.");
            Add("settings.service.noservice", "NetForge no se instala como servicio del sistema a proposito: el punto de acceso y la bandeja necesitan tu sesion de usuario. Para el arranque en segundo plano se usa la tarea programada de abajo.", "NetForge deliberately does not install itself as a system service: the access point and the tray need your user session. Background startup is handled by the scheduled task below.");
            Add("settings.section.files", "Archivos", "Files");
            Add("settings.section.startup", "Arranque con Windows", "Starting with Windows");
            Add("settings.section.alerts", "Avisos", "Alerts");
            Add("settings.autostart.state", "Tarea programada", "Scheduled task");
            Add("settings.autostart.current", "activa, con privilegios altos y la ruta correcta", "active, elevated, with the right path");
            Add("settings.autostart.otherpath", "apunta a otra ruta: {0}", "points to another path: {0}");
            Add("settings.autostart.missing", "no existe", "does not exist");
            Add("settings.autostart.unknown", "existe, pero no se pudo leer", "it exists, but could not be read");
            Add("settings.autostart.repair", "Reparar la tarea", "Repair the task");
            Add("settings.autostart.repaired", "Tarea de arranque recreada con la ruta actual.", "Startup task recreated with the current path.");
            Add("settings.autostart.why", "Se usa una tarea programada con privilegios altos en lugar de la clave Run del registro: asi arranca sin pedir permisos en cada inicio de sesion, y no hace falta instalar ningun servicio.", "A scheduled task with high privileges is used instead of the Run registry key: it starts without asking for permission at every logon, and no service has to be installed.");
            Add("settings.filesauto", "Levantar los servidores al crear la red", "Start the servers when the network comes up");
            Add("settings.ftpuser", "Usuario del FTP", "FTP user");
            Add("settings.notify", "Avisos de Windows en la bandeja", "Windows tray notifications");
            Add("settings.notifyjoin", "Avisar cuando entra un dispositivo nuevo", "Notify when a new device joins");
            Add("settings.notifynote", "Los avisos se limitan a uno cada veinte segundos para no convertirse en ruido; el registro de actividad guarda todo igualmente.", "Alerts are limited to one every twenty seconds so they do not become noise; the activity log keeps everything anyway.");
            Add("settings.section.timing", "Ritmo de trabajo", "Working rhythm");
            Add("settings.section.app", "Aplicacion", "Application");
            Add("settings.language.reload", "El idioma cambia al vuelo: la ventana se reconstruye en el idioma elegido.", "The language changes on the fly: the window is rebuilt in the chosen language.");
            Add("clients.banned", "BLOQUEADO", "BLOCKED");
            Add("clients.filter", "Buscar por IP, MAC, fabricante o nombre", "Search by IP, MAC, vendor or hostname");
            Add("clients.clear", "Limpiar", "Clear");
            Add("clients.menu.ban", "Banear (El Martillo)", "Ban (The Hammer)");
            Add("clients.menu.pardon", "Indultar a este dispositivo", "Pardon this device");
            Add("clients.menu.copyip", "Copiar la IP", "Copy the IP");
            Add("clients.menu.copymac", "Copiar la MAC", "Copy the MAC");
            Add("clients.menu.open", "Abrir su IP en el navegador", "Open its IP in the browser");
            Add("clients.menu.select", "Elige un dispositivo en la lista.", "Pick a device from the list.");
            Add("clients.copied", "Copiado al portapapeles: {0}", "Copied to the clipboard: {0}");
            Add("status.memory", "Memoria", "Memory");
            Add("status.ports", "Puertos", "Ports");
            Add("common.ok", "Vale", "OK");
            Add("common.error", "Error", "Error");
            Add("common.warning", "Aviso", "Warning");
            Add("common.question", "Confirmar", "Confirm");
            Add("common.report", "Informe", "Report");
            Add("autostart", "Arrancar al iniciar Windows (sin pedir permisos)", "Start with Windows (no prompt)");
            Add("exit.title", "Salir de NetForge Studio", "Exit NetForge Studio");
            Add("exit.text", "Se cerrara la red local y los servidores de archivos. Continuar?", "The local network and file servers will be closed. Continue?");
        }

        private static void Add(string key, string spanish, string english)
        {
            Table[key] = new string[] { spanish, english };
        }

        private static string _override;

        /// <summary>
        /// Idioma elegido por el usuario: "es", "en" o vacio para seguir al sistema. Se aplica
        /// antes de crear la interfaz, porque los textos se piden una sola vez al construirla.
        /// </summary>
        public static void ApplyLanguage(string preference)
        {
            if (!string.IsNullOrEmpty(preference) &&
                (preference == "es" || preference == "en"))
            {
                _override = preference;
                return;
            }

            _override = null;
        }

        public static string Language
        {
            get { return _override ?? string.Empty; }
        }

        public static bool IsSpanish
        {
            get
            {
                if (_override != null)
                {
                    return _override == "es";
                }

                return Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "es";
            }
        }

        public static string T(string key)
        {
            string[] entry;
            if (Table.TryGetValue(key, out entry))
            {
                return IsSpanish ? entry[0] : entry[1];
            }

            return key;
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), args);
        }
    }
}
