namespace DotNet.Status.Web.Models;
public class GrafanaAnnotation
{
    public long Time { get; set; }
    
    public long? TimeEnd { get; set; }
    
    public string Title { get; set; }
    
    public string[] Tags { get; set; }
    
    public string Text { get; set; }
}
