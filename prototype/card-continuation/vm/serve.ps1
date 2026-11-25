# PROTOTYPE helper (ReparoHome#67 (as #66)): serve web\spend.html on http://localhost:8066/ for the Use Browser probe.
$l = [Net.HttpListener]::new(); $l.Prefixes.Add('http://localhost:8066/'); $l.Start()
$page = [IO.File]::ReadAllBytes('C:\Users\reparo\UiPath\reparo\proto48\web\spend.html')
while ($l.IsListening) { $c = $l.GetContext(); $c.Response.ContentType = 'text/html'; $c.Response.OutputStream.Write($page, 0, $page.Length); $c.Response.Close() }
