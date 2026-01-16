using System;

namespace DotNet.Status.Web.Models;
public class GrafanaAnnotationQuery
{
    public AnnotationQueryRange Range { get; set; }
    public AnnotationDefinition Annotation { get; set; }
}

public class AnnotationQueryRange
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

public class AnnotationDefinition
{
    public string Name { get; set; }
    public string Datasource { get; set; }
    public bool Enable { get; set; }
    public string IconColor { get; set; }
    public string Query { get; set; }
}
