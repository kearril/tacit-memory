// The tests run in Node, which has no page. The API client sends absolute addresses on the page's origin, so the tests are
// given one before any module loads.
Object.assign(globalThis, { location: { origin: "http://tacit.test" } });
