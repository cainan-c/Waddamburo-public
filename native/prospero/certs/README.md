# Console HTTPS trust roots

`cacert.pem` is the Mozilla CA root extract distributed by curl, retrieved from
https://curl.se/ca/cacert.pem on 2026-10-20 (bundle timestamp 13:32:40 GMT).
SHA-256: `796f1389bfea13c0ed78f663b765baaff54a508e5483786bd6ece73c5e3e3cbe`.
The bundle carries its upstream notices and uses MPL-2.0 (license text beside it).

The PS5 backend embeds this file, validates its Base64 payload and supplies
complete CRLF PEM records without OpenSSL. One native pointer array registers
the entire CA set in each HTTP/TLS context. Records and descriptor tables remain
allocated until context teardown, including import failures. Hostname, validity
and trusted-chain verification remain enabled. Updating roots requires reviewing
and replacing this source file; there is no unauthenticated runtime CA download.
