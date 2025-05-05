# core-signatures

A Package core module for handling signatures

## Usage Backend
```C#
using CoreSignatures;


SignatureService _signatureService = new();
var request = new Request()
{
    Headers = context.Request.Headers.ToDictionary(a => a.Key, a => a.Value.ToString()),
    Body = context.Request.Body,
    Query = context.Request.Query
};
if (_signatureService.UseSignature(request)) 
    await this._signatureService.VerifySignature(request);

```

## Usage Frontend
```C#
SignatureService _signatureService = new();
string query = JsonSerializer.Serialize(request.Query);
string body = await this._signatureService.ReadJsonBodyAsync(request.Body as FileBufferingReadStream);
string publicKey = "e73ab23c5dd70727dfa360d4b6c18612de864768bd0d9e2cb1c507a02c3a227d";
await SignatureFunctions.EncryptSignature(query, body, publicKey);
```


# Feature Flag

## Usage Backend
```C#
IFeatureFlag _feaureFlag = new FeatureFlag();
IContextFeatureFlag ctx = new();
if (_featureFlag.GetStatusFlag(ctx, 'feature-flag', true)) {
    // enable
}

```