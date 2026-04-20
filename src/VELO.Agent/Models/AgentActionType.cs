namespace VELO.Agent.Models;

public enum AgentActionType
{
    OpenTab,        // Abrir una URL en nueva pestaña
    Search,         // Buscar texto en el motor configurado
    Summarize,      // Resumir el contenido de la página activa
    FillForm,       // Rellenar campo de formulario con valor
    ClickElement,   // Hacer clic en un selector CSS
    ScrollTo,       // Hacer scroll a un selector o posición
    CopyToClipboard,// Copiar texto al portapapeles
    ReadPage,       // Leer el contenido de la página activa (sin acción visible)
    Respond,        // Respuesta de texto pura al usuario (sin acción en el browser)
}
