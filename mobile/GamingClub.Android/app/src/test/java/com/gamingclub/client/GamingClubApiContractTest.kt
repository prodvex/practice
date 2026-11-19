package com.gamingclub.client

import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.json.JSONObject
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test

/**
 * Local JVM HTTP contract checks. No SQL Server, emulator or live API needed.
 * They intentionally exercise the real HttpURLConnection + JSON implementation.
 */
class GamingClubApiContractTest {
    private lateinit var server: MockWebServer
    private lateinit var api: GamingClubApi

    @Before fun setUp() {
        server = MockWebServer()
        server.start()
        api = GamingClubApi(server.url("/").toString())
    }

    @After fun tearDown() {
        server.shutdown()
    }

    private fun success(body: String): MockResponse =
        MockResponse().setResponseCode(200).setHeader("Content-Type", "application/json")
            .setBody(body)

    private fun authenticated() {
        api.acceptAuth(JSONObject().put("accessToken", "jwt-in-memory-for-test"))
    }

    @Test fun loginDoesNotPersistTokenUntilUiAcceptsResponse() {
        server.enqueue(success("""{"accessToken":"jwt-for-test","role":"VISITOR","login":"user18"}"""))
        val response = api.login("user18", "password-for-test")
        assertNull(api.accessToken)
        api.acceptAuth(response)
        assertEquals("jwt-for-test", api.accessToken)

        val request = server.takeRequest()
        assertEquals("POST", request.method)
        assertEquals("/api/auth/login", request.path)
        assertNull(request.getHeader("Authorization"))
        assertEquals("user18", JSONObject(request.body.readUtf8()).getString("login"))
    }

    @Test fun registrationSendsExpectedPayload() {
        server.enqueue(success("""{"accessToken":"jwt-for-test"}"""))
        api.register("new18", "strong-example-password", "Test Visitor")
        val request = server.takeRequest()
        assertEquals("/api/auth/register", request.path)
        val body = JSONObject(request.body.readUtf8())
        assertEquals("new18", body.getString("login"))
        assertEquals("Test Visitor", body.getString("displayName"))
        assertFalse(body.has("role"))
    }

    @Test fun existingTokenIsNotAttachedToLoginRequest() {
        authenticated()
        server.enqueue(success("""{"accessToken":"new-jwt"}"""))
        api.login("other18", "password-for-test")
        assertNull(server.takeRequest().getHeader("Authorization"))
    }

    @Test fun bookingUsesProtectedEndpointWithoutClientUserId() {
        authenticated()
        server.enqueue(success("""{"bookingId":5,"priceRub":120}"""))
        val response = api.book(12, 4, "2030-01-05T12:00:00Z", "2030-01-05T13:00:00Z")
        assertEquals(5, response.getInt("bookingId"))
        val request = server.takeRequest()
        assertEquals("POST", request.method)
        assertEquals("/api/bookings", request.path)
        assertEquals("Bearer jwt-in-memory-for-test", request.getHeader("Authorization"))
        val body = JSONObject(request.body.readUtf8())
        assertEquals(12, body.getInt("seatId"))
        assertEquals(4, body.getInt("tariffId"))
        assertEquals("2030-01-05T12:00:00Z", body.getString("startUtc"))
        assertFalse(body.has("userId"))
    }

    @Test fun ownBookingsUsesAuthenticatedGet() {
        authenticated()
        server.enqueue(success("""[{"bookingId":9,"status":"ACTIVE"}]"""))
        assertEquals(9, api.mine().getJSONObject(0).getInt("bookingId"))
        val request = server.takeRequest()
        assertEquals("GET", request.method)
        assertEquals("/api/bookings/mine", request.path)
        assertEquals("Bearer jwt-in-memory-for-test", request.getHeader("Authorization"))
    }

    @Test fun cancellationUsesAuthenticatedPost() {
        authenticated()
        server.enqueue(success("""{"error":0,"voidedInvoices":1}"""))
        assertEquals(1, api.cancelBooking(18).getInt("voidedInvoices"))
        val request = server.takeRequest()
        assertEquals("POST", request.method)
        assertEquals("/api/bookings/18/cancel", request.path)
        assertEquals("Bearer jwt-in-memory-for-test", request.getHeader("Authorization"))
    }

    @Test fun extensionPreservesUtcTimestamp() {
        authenticated()
        server.enqueue(success("""{"success":true,"extraChargeRub":60}"""))
        api.extend(12, "2030-01-05T14:30:00Z")
        val request = server.takeRequest()
        assertEquals("/api/sessions/12/extend", request.path)
        assertEquals("2030-01-05T14:30:00Z",
            JSONObject(request.body.readUtf8()).getString("newEndUtc"))
    }

    @Test fun finishUsesAuthenticatedPost() {
        authenticated()
        server.enqueue(success("""{"success":true,"message":"Session finished."}"""))
        assertTrue(api.finishSession(12).getBoolean("success"))
        val request = server.takeRequest()
        assertEquals("POST", request.method)
        assertEquals("/api/sessions/12/finish", request.path)
        assertEquals("Bearer jwt-in-memory-for-test", request.getHeader("Authorization"))
    }

    @Test fun bookingConflictReturnsHttp409() {
        authenticated()
        server.enqueue(MockResponse().setResponseCode(409)
            .setHeader("Content-Type", "application/json")
            .setBody("""{"error":"Seat already reserved."}"""))
        try {
            api.book(1, 1, "2030-01-05T12:00:00Z", "2030-01-05T13:00:00Z")
            fail("Expected API conflict")
        } catch (error: ApiException) {
            assertEquals(409, error.status)
            assertEquals("Seat already reserved.", error.message)
        }
    }

    @Test fun expiredTokenProducesUnauthorized() {
        authenticated()
        server.enqueue(MockResponse().setResponseCode(401))
        try {
            api.mine()
            fail("Expected 401")
        } catch (error: ApiException) {
            assertEquals(401, error.status)
        }
    }
}
