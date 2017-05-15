# Web Hooks with SQL Stream Store

A sample project that has a WebHook Publisher and Subscriber:

 1. HTTP API to create subscriber endpoints with unique URLs
 2. HTTP API to create webhooks on the publisher using the subscriber endpoint URLs.
 3. Publisher events are written to the webhook's `out` stream.
 4. A delivery function pushes events to the subscriber endpoints.
 5. Deliveries, including failures, are written to the webhook's `delivery` stream.
 6. Delivery failures are retried with exponential back-off.
 7. HTTP APIs to read the `out` and `delivery` stream for UI.
 8. Streams have limits applied - max count and max age. Undelivered events are automatically purged.
 9. Subscribers in long term failure are disabled after a configurable timespan.
 10. Subscribers leverage SQL Stream Store to handle idempotent receiving. 
 
TODO: message resending, more extensive error handling.
