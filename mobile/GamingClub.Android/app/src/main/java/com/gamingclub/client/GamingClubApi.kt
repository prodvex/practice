package com.gamingclub.client

import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/** Small HTTP client for the local GamingClub development API. */
class GamingClubApi(private val baseUrl: String) {
    var accessToken: String? = null
        private set

    fun clearToken() { accessToken = null }

    fun login(login: String, password: String): JSONObject {
        val response = JSONObject(request("POST", "/api/auth/login",
            JSONObject().put("login", login).put("password", password)))
        accessToken = response.getString("accessToken")
        return response
    }

    fun register(login: String, password: String, displayName: String): JSONObject {
        val response = JSONObject(request("POST", "/api/auth/register", JSONObject()
            .put("login", login).put("password", password).put("displayName", displayName)))
        accessToken = response.getString("accessToken")
        return response
    }

    fun mine(): JSONArray = JSONArray(request("GET", "/api/bookings/mine"))

    private fun request(method: String, path: String, body: JSONObject? = null): String {
        val connection = URL(baseUrl.trimEnd('/') + path).openConnection() as HttpURLConnection
        connection.requestMethod = method
        connection.connectTimeout = 5000
        connection.readTimeout = 5000
        connection.setRequestProperty("Accept", "application/json")
        accessToken?.let { connection.setRequestProperty("Authorization", "Bearer $it") }
        try {
            if (body != null) {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                connection.outputStream.use {
                    it.write(body.toString().toByteArray(Charsets.UTF_8))
                }
            }
            val status = connection.responseCode
            val text = (if (status in 200..299) connection.inputStream else connection.errorStream)
                ?.bufferedReader(Charsets.UTF_8)?.use { it.readText() }.orEmpty()
            if (status !in 200..299)
                throw ApiException(status, runCatching {
                    JSONObject(text).optString("error", "HTTP $status")
                }.getOrDefault("HTTP $status"))
            return text
        } finally {
            connection.disconnect()
        }
    }

    fun seats(): JSONArray = JSONArray(request("GET", "/api/seats"))
    fun tariffs(): JSONArray = JSONArray(request("GET", "/api/tariffs"))

    /** Authenticated booking; server derives ownership from the bearer token. */
    fun book(userId: Int, seatId: Int, tariffId: Int, fromUtc: String, toUtc: String): JSONObject =
        JSONObject(request("POST", "/api/bookings", JSONObject().apply {
            put("userId", userId)
            put("seatId", seatId)
            put("tariffId", tariffId)
            put("startUtc", fromUtc)
            put("endUtc", toUtc)
        }))

    fun startSession(bookingId: Int) =
        JSONObject(request("POST", "/api/sessions/$bookingId/start", JSONObject()))

    fun clock(bookingId: Int) =
        JSONObject(request("GET", "/api/sessions/$bookingId"))

    fun extend(bookingId: Int, newEndUtc: String) =
        JSONObject(request("POST", "/api/sessions/$bookingId/extend",
            JSONObject().put("newEndUtc", newEndUtc)))
}

class ApiException(val status: Int, override val message: String) : Exception(message)
