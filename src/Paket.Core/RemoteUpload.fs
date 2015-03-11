﻿module Paket.RemoteUpload

open System
open System.Globalization
open System.IO
open System.Net
open System.Text
open Paket
open Paket.Logging

type System.Net.WebClient with
        member x.UploadFileAsMultipart (url:Uri) filename =
            let fileTemplate = "--{0}\r\nContent-Disposition: form-data; name=\"{1}\"; filename=\"{2}\"\r\nContent-Type: {3}\r\n\r\n"
            let boundary = "---------------------------" + DateTime.Now.Ticks.ToString("x", CultureInfo.InvariantCulture)
            let fileInfo = (new FileInfo(Path.GetFullPath(filename)))
            let fileHeaderBytes = String.Format(CultureInfo.InvariantCulture, fileTemplate, boundary, "package", "package", "application/octet-stream")
                                  |> Encoding.UTF8.GetBytes
            let newlineBytes = Environment.NewLine |> Encoding.UTF8.GetBytes
            let trailerbytes = String.Format(CultureInfo.InvariantCulture, "--{0}--", boundary) |> Encoding.UTF8.GetBytes
            x.Headers.Add(HttpRequestHeader.ContentType, "multipart/form-data; boundary=" + boundary);
            use stream = x.OpenWrite(url, "PUT")
            stream.Write(fileHeaderBytes,0,fileHeaderBytes.Length)

            use fileStream = File.OpenRead fileInfo.FullName
            let totalLength = fileStream.Length
            let buffer = Array.zeroCreate<byte> 4096
            let totalSoFar = ref (int64 0)
            let read = ref (fileStream.Read(buffer, 0, buffer.Length))
            let lastReported = ref (int64 0)
            while !read > 0 do 
                totalSoFar := !totalSoFar + (int64 !read)
                stream.Write(buffer, 0, !read)

                let progress = !totalSoFar * (int64 100) / totalLength
                if progress <> !lastReported then
                    tracefn "  Progress: %d%%" progress
                    lastReported := progress
                read := fileStream.Read(buffer, 0, buffer.Length)

            stream.Write(newlineBytes, 0, newlineBytes.Length)
            stream.Write(trailerbytes, 0, trailerbytes.Length)
            ()

let GetUrlWithEndpoint (url: string option) (endPoint: string option) =
    let (|UrlWithEndpoint|_|) url = 
        match url with
        | Some url when not (String.IsNullOrEmpty(Uri(url).AbsolutePath.TrimStart('/'))) -> Some(Uri(url)) 
        | _                                                                              -> None  

    let (|IsUrl|_|) (url: string option) =
        match url with
        | Some url -> Uri(url.TrimEnd('/') + "/") |> Some
        | _        -> None
    
    let defaultEndpoint = "/api/v2/package" 
    let urlWithEndpoint = 
        match (url, endPoint) with
        | None                   , _                   -> Uri(Uri("https://nuget.org"), defaultEndpoint)
        | IsUrl baseUrl          , Some customEndpoint -> Uri(baseUrl, customEndpoint.TrimStart('/'))
        | UrlWithEndpoint baseUrl, _                   -> baseUrl
        | IsUrl baseUrl          , None                -> Uri(baseUrl, defaultEndpoint)
        | Some whyIsThisNeeded   , _                   -> failwith "Url and endpoint combination not supported"  
    urlWithEndpoint.ToString ()

  
let Push maxTrials url apiKey packageFileName =
    let rec push trial =
        tracefn "Pushing package %s to %s - trial %d" packageFileName url trial
        try
            let client = Utils.createWebClient(url, None)
            client.Headers.Add("X-NuGet-ApiKey", apiKey)
            client.UploadFileAsMultipart (new Uri(url)) packageFileName
            |> ignore
            tracefn "Pushing %s complete." packageFileName
        with
        | exn when trial < maxTrials ->             
            traceWarnfn "Could not push %s: %s" packageFileName exn.Message            
            push (trial + 1)

    push 1