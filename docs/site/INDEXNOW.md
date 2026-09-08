# IndexNow

`4beb5c5b3dc4f80280c6c609393a2aa1.txt` is an IndexNow key. It authorises this site to tell participating search
engines - Bing, Yandex, Seznam and Naver - that a page has changed, instead of waiting
for them to come back and look.

The key file has to stay reachable at

    https://ahmetcaglayan.github.io/Hexnest/4beb5c5b3dc4f80280c6c609393a2aa1.txt

because a submission is only accepted for URLs underneath the directory the key file
sits in. Deleting or renaming it silently stops submissions being honoured.

To submit after a change:

    curl -H 'Content-Type: application/json' -d @- https://api.indexnow.org/indexnow <<'JSON'
    {
      "host": "ahmetcaglayan.github.io",
      "key": "4beb5c5b3dc4f80280c6c609393a2aa1",
      "keyLocation": "https://ahmetcaglayan.github.io/Hexnest/4beb5c5b3dc4f80280c6c609393a2aa1.txt",
      "urlList": ["https://ahmetcaglayan.github.io/Hexnest/"]
    }
    JSON

Google does not take part. Its sitemap ping endpoint was retired in January 2024 and
returns 404; Bing's returns 410. The only way to ask Google is Search Console.
