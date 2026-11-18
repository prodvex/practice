package com.gamingclub.client

import android.app.Activity
import android.app.AlertDialog
import android.graphics.Color
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.widget.*
import java.time.Instant
import java.time.temporal.ChronoUnit

/** Development-only Android client backed by GamingClub API. */
class MainActivity : Activity() {
    private val api by lazy { GamingClubApi(BuildConfig.API_BASE_URL) }
    private lateinit var status: TextView
    private lateinit var loginName: EditText
    private lateinit var loginPassword: EditText
    private lateinit var displayName: EditText
    private lateinit var myBookings: LinearLayout
    private lateinit var profileText: TextView
    private lateinit var selectedSeatText: TextView
    private lateinit var contentScroll: ScrollView
    private val navigationButtons = linkedMapOf<String, Button>()
    private var authenticated = false
    private var userLogin = ""
    private var userRole = ""
    // Never accept a response from an earlier user session or a superseded login.
    private var authEpoch = 0L
    private val loginGuard = AuthAttemptGuard()
    private val screens = linkedMapOf<String, LinearLayout>()
    private lateinit var seatsView: LinearLayout
    private lateinit var hallPlan: HallPlanView
    private lateinit var sessionView: TextView
    private lateinit var tariffsView: Spinner
    private lateinit var startUtc: EditText
    private lateinit var endUtc: EditText
    private val tariffs = mutableListOf<Int>()
    private var seatId: Int? = null
    private var bookingId: Int? = null
    private var bookedEnd: Instant? = null
    private val timerHandler = Handler(Looper.getMainLooper())
    private var clockPolling = false
    private var loadingClock = false
    private var sessionRunning = false
    private val clockPoll = object : Runnable {
        override fun run() {
            if (!clockPolling || isFinishing || isDestroyed) return
            val currentBooking = bookingId
            if (currentBooking != null && !loadingClock) {
                loadingClock = true
                Thread {
                    val response = runCatching { api.clock(currentBooking) }
                    runOnUiThread {
                        loadingClock = false
                        if (clockPolling && bookingId == currentBooking) {
                            response.onSuccess { value ->
                                val remaining = value.getInt("remainingSeconds")
                                sessionView.text = "Сессия №${value.getInt("sessionId")}\n" +
                                    SessionClockFormatter.format(remaining)
                                if (remaining <= 0) {
                                    sessionRunning = false
                                    stopClockPolling()
                                }
                            }.onFailure { status.text = "Таймер: ${it.message}" }
                        }
                    }
                }.start()
            }
            if (clockPolling) timerHandler.postDelayed(this, 5000)
        }
    }

    private fun startClockPolling() {
        if (clockPolling) return
        clockPolling = true
        timerHandler.post(clockPoll)
    }

    private fun stopClockPolling() {
        clockPolling = false
        timerHandler.removeCallbacks(clockPoll)
    }

    override fun onStop() {
        stopClockPolling()
        super.onStop()
    }

    override fun onDestroy() {
        stopClockPolling()
        super.onDestroy()
    }

    override fun onStart() {
        super.onStart()
        if (::sessionView.isInitialized && sessionRunning && bookingId != null) startClockPolling()
    }

    private fun label(value: String) = TextView(this).apply {
        text = value
        ClubUi.label(this)
    }
    private fun input(value: String) = EditText(this).apply {
        setText(value)
        ClubUi.input(this)
    }
    private fun <T> execute(
        task: () -> T,
        errorHandler: ((Exception) -> Unit)? = null,
        complete: (T) -> Unit
    ) {
        val epoch = authEpoch
        Thread {
            try {
                val value = task()
                runOnUiThread {
                    if (epoch == authEpoch && !isFinishing && !isDestroyed) complete(value)
                }
            } catch (ex: Exception) {
                runOnUiThread {
                    if (epoch != authEpoch || isFinishing || isDestroyed) return@runOnUiThread
                    if (errorHandler != null) {
                        errorHandler(ex)
                    } else if (ex is ApiException && ex.status == 401 && authenticated) {
                        signOut("Срок действия входа истёк. Войдите снова.")
                    } else {
                        status.text = "Ошибка API: ${ex.message}"
                    }
                }
            }
        }.start()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!BuildConfig.DEBUG) {
            setContentView(label("Тестовый клиент доступен только в Debug."))
            return
        }
        val outer = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(ClubUi.background)
            setPadding(ClubUi.dp(this@MainActivity, 14),
                ClubUi.dp(this@MainActivity, 8),
                ClubUi.dp(this@MainActivity, 14),
                ClubUi.dp(this@MainActivity, 10))
        }
        setContentView(outer)
        outer.addView(label("GamingClub · Вариант 18").apply {
            textSize = 22f
            setTypeface(null, android.graphics.Typeface.BOLD)
            setTextColor(ClubUi.primary)
        })
        status = label("Войдите или зарегистрируйтесь").apply {
            textSize = 13f
            setTextColor(ClubUi.muted)
        }
        outer.addView(status)
        contentScroll = ScrollView(this).apply {
            isFillViewport = true
            clipToPadding = false
        }
        outer.addView(contentScroll, LinearLayout.LayoutParams(-1, 0, 1f))
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(ClubUi.dp(this@MainActivity, 2), 0,
                ClubUi.dp(this@MainActivity, 2), 0)
        }
        contentScroll.addView(content, android.widget.FrameLayout.LayoutParams(-1, -2))
        val nav = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            ClubUi.card(this)
            setPadding(ClubUi.dp(this@MainActivity, 2),
                ClubUi.dp(this@MainActivity, 4),
                ClubUi.dp(this@MainActivity, 2),
                ClubUi.dp(this@MainActivity, 4))
        }
        outer.addView(nav, LinearLayout.LayoutParams(-1, ClubUi.dp(this, 64)))
        val labels = listOf(
            "Вход", "Зал", "Бронирование", "Мои брони", "Сессия", "Профиль"
        )
        val shortLabels = listOf("Вход", "Зал", "Бронь", "Мои", "Сессия", "Профиль")
        for ((index, name) in labels.withIndex()) {
            screens[name] = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                visibility = android.view.View.GONE
            }
            content.addView(screens.getValue(name),
                LinearLayout.LayoutParams(-1, -2))
            val tab = Button(this).apply {
                text = shortLabels[index]
                ClubUi.navigation(this, false)
                contentDescription = "Раздел: $name"
                setOnClickListener {
                    if (!authenticated && name != "Вход") {
                        status.text = "Сначала войдите в аккаунт"
                        showScreen("Вход")
                    } else {
                        showScreen(name)
                        if (name == "Мои брони") loadMine()
                    }
                }
            }
            navigationButtons[name] = tab
            nav.addView(tab, LinearLayout.LayoutParams(0, ClubUi.dp(this, 52), 1f))
        }
        val login = screens.getValue("Вход")
        login.addView(label("Войти / зарегистрироваться").apply { ClubUi.label(this, heading = true) })
        loginName = input("")
        loginName.hint = "Логин"
        login.addView(loginName)
        loginPassword = input("")
        loginPassword.hint = "Пароль (не менее 12 символов для регистрации)"
        loginPassword.inputType = android.text.InputType.TYPE_CLASS_TEXT or
            android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD
        login.addView(loginPassword)
        displayName = input("")
        displayName.hint = "Имя для регистрации"
        login.addView(displayName)
        login.addView(Button(this).apply {
            ClubUi.button(this)
            text = "Войти"
            setOnClickListener { authenticate(false) }
        })
        login.addView(Button(this).apply {
            ClubUi.button(this)
            text = "Создать аккаунт посетителя"
            setOnClickListener { authenticate(true) }
        })

        val hall = screens.getValue("Зал")
        hall.addView(label("1. Схема зала").apply { ClubUi.label(this, heading = true) })
        hall.addView(Button(this).apply {
            ClubUi.button(this)
            text = "Обновить зал"
            ClubUi.button(this, false)
            setOnClickListener { refresh() }
        })
        hallPlan = HallPlanView(this).apply {
            onSeatSelected = { id, number ->
                seatId = id
                selectedSeatText.text = "Выбран ПК №$number"
                status.text = "Выбран ПК №$number. Доступность проверит сервер."
                showScreen("Бронирование")
            }
        }
        hall.addView(hallPlan)
        hall.addView(label("Зелёный — активен, серый — отключён, жёлтый — выбран."))
        seatsView = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        hall.addView(seatsView)

        val booking = screens.getValue("Бронирование")
        booking.addView(label("2. Выбор тарифа и времени UTC").apply { ClubUi.label(this, heading = true) })
        selectedSeatText = label("Компьютер ещё не выбран").apply {
            setTextColor(ClubUi.primary)
            setTypeface(null, android.graphics.Typeface.BOLD)
        }
        booking.addView(selectedSeatText)
        tariffsView = Spinner(this).apply {
            setBackgroundColor(ClubUi.surface)
            minimumHeight = ClubUi.dp(this@MainActivity, 48)
        }
        booking.addView(tariffsView)
        val start = Instant.now().truncatedTo(ChronoUnit.MINUTES).plus(2, ChronoUnit.MINUTES)
        booking.addView(label("Начало UTC, например 2026-11-20T14:00:00Z"))
        startUtc = input(start.toString())
        booking.addView(startUtc)
        booking.addView(label("Конец UTC"))
        endUtc = input(start.plus(1, ChronoUnit.HOURS).toString())
        booking.addView(endUtc)
        booking.addView(Button(this).apply {
            ClubUi.button(this)
            text = "Забронировать выбранный ПК"
            setOnClickListener { book() }
        })

        val mine = screens.getValue("Мои брони")
        mine.addView(label("3. Мои бронирования").apply { ClubUi.label(this, heading = true) })
        mine.addView(Button(this).apply {
            ClubUi.button(this)
            text = "Обновить список"
            ClubUi.button(this, false)
            setOnClickListener { loadMine() }
        })
        myBookings = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        mine.addView(myBookings)

        val session = screens.getValue("Сессия")
        session.addView(label("4. Текущая сессия").apply { ClubUi.label(this, heading = true) })
        sessionView = label("Выберите бронь или создайте новую").apply {
            textSize = 24f
            setTypeface(null, android.graphics.Typeface.BOLD)
            setTextColor(ClubUi.primary)
            ClubUi.card(this)
        }
        session.addView(sessionView)
        session.addView(Button(this).apply {
            ClubUi.button(this)
            text = "Запустить сессию"
            setOnClickListener { sessionAction("start") }
        })
        session.addView(Button(this).apply {
            ClubUi.button(this)
            text = "Обновить таймер"
            ClubUi.button(this, false)
            setOnClickListener { sessionAction("clock") }
        })
        session.addView(Button(this).apply {
            ClubUi.button(this)
            text = "Продлить на 30 минут"
            setOnClickListener { sessionAction("extend") }
        })

        val profile = screens.getValue("Профиль")
        profile.addView(label("5. Профиль").apply { ClubUi.label(this, heading = true) })
        profileText = label("Вы не вошли")
        profile.addView(profileText)
        profile.addView(Button(this).apply {
            ClubUi.button(this)
            text = "Выйти"
            ClubUi.button(this, false)
            setOnClickListener {
                signOut("Сессия входа завершена")
            }
        })
        showScreen("Вход")
    }

    private fun showScreen(name: String) {
        screens.forEach { (key, view) ->
            view.visibility = if (key == name) android.view.View.VISIBLE else android.view.View.GONE
        }
        navigationButtons.forEach { (key, button) ->
            ClubUi.navigation(button, key == name)
        }
        if (::contentScroll.isInitialized) contentScroll.scrollTo(0, 0)
    }

    private fun signOut(message: String) {
        authEpoch += 1
        loginGuard.invalidate()
        stopClockPolling()
        api.clearToken()
        authenticated = false
        sessionRunning = false
        loadingClock = false
        bookingId = null
        bookedEnd = null
        seatId = null
        tariffs.clear()
        myBookings.removeAllViews()
        seatsView.removeAllViews()
        loginName.text.clear()
        loginPassword.text.clear()
        displayName.text.clear()
        selectedSeatText.text = "Компьютер ещё не выбран"
        sessionView.text = "Войдите для просмотра сессии"
        profileText.text = "Вы не вошли"
        userLogin = ""
        userRole = ""
        status.text = message
        showScreen("Вход")
    }

    private fun authenticate(register: Boolean) {
        val login = loginName.text.toString().trim()
        val password = loginPassword.text.toString()
        val name = displayName.text.toString().trim()
        if (login.isBlank() || password.isBlank() || (register && name.isBlank())) {
            status.text = "Заполните обязательные поля"
            return
        }
        // Switching accounts must invalidate in-flight calls from the old identity.
        if (authenticated) signOut("Переключение аккаунта...")
        val attempt = loginGuard.begin()
        execute({
            if (register) api.register(login, password, name) else api.login(login, password)
        }, errorHandler = { ex ->
            if (loginGuard.isCurrent(attempt))
                status.text = "Ошибка входа: ${ex.message}"
        }) { result ->
            if (!loginGuard.isCurrent(attempt)) return@execute
            if (result.optString("accessToken").isBlank()) {
                status.text = "Сервер не вернул токен доступа."
                return@execute
            }
            authEpoch += 1
            api.acceptAuth(result)
            authenticated = true
            userLogin = result.getString("login")
            userRole = result.getString("role")
            profileText.text = "Логин: $userLogin · Роль: $userRole"
            loginPassword.text.clear()
            status.text = "Вход выполнен: $userLogin"
            showScreen("Зал")
            refresh()
        }
    }

    private fun loadMine() {
        if (!authenticated) return
        val requestedToken = api.accessToken
        execute({ api.mine() }) { rows ->
            if (!authenticated || api.accessToken != requestedToken) return@execute
            myBookings.removeAllViews()
            for (i in 0 until rows.length()) {
                val booking = rows.getJSONObject(i)
                val id = booking.getInt("bookingId")
                val to = booking.getString("endUtc")
                val row = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
                row.addView(Button(this).apply {
            ClubUi.button(this)
                    text = "Бронь №$id · ПК ${booking.getInt("seatId")} · до $to"
                    setOnClickListener {
                        bookingId = id
                        bookedEnd = runCatching { Instant.parse(to) }.getOrNull()
                        showScreen("Сессия")
                        sessionAction("clock")
                    }
                })
                val starts = runCatching { Instant.parse(booking.getString("startUtc")) }.getOrNull()
                if (booking.getString("status") == "ACTIVE" &&
                    starts != null && starts.isAfter(Instant.now())) {
                    row.addView(Button(this).apply {
            ClubUi.button(this)
                        text = "Отменить бронь №$id"
                        ClubUi.button(this, false)
                        setOnClickListener {
                            AlertDialog.Builder(this@MainActivity)
                                .setTitle("Отмена бронирования")
                                .setMessage("Отменить бронь №$id? Неоплаченные начисления будут аннулированы.")
                                .setNegativeButton("Нет", null)
                                .setPositiveButton("Отменить") { _, _ ->
                                    execute({ api.cancelBooking(id) }) {
                                        if (bookingId == id) {
                                            stopClockPolling()
                                            sessionRunning = false
                                            bookingId = null
                                            bookedEnd = null
                                            sessionView.text = "Бронь №$id отменена"
                                        }
                                        status.text = "Бронь №$id отменена"
                                        loadMine()
                                    }
                                }
                                .show()
                        }
                    })
                }
                myBookings.addView(row)
            }
            status.text = "Найдено ваших бронирований: ${rows.length()}"
        }
    }

    private fun refresh() {
        status.text = "Получение мест и тарифов..."
        execute({ api.seats() to api.tariffs() }) { (seats, prices) ->
            seatsView.removeAllViews()
            seatId = null
            selectedSeatText.text = "Компьютер ещё не выбран"
            hallPlan.submit(seats)
            tariffs.clear()
            val names = mutableListOf<String>()
            for (i in 0 until prices.length()) {
                val tariff = prices.getJSONObject(i)
                tariffs += tariff.getInt("tariffId")
                names += "${tariff.getString("name")} — ${tariff.getDouble("priceRub")} ₽"
            }
            val adapter = object : ArrayAdapter<String>(
                this, android.R.layout.simple_spinner_item, names) {
                override fun getView(position: Int, oldView: android.view.View?,
                    parent: android.view.ViewGroup): android.view.View {
                    return super.getView(position, oldView, parent).also { view ->
                        (view as? TextView)?.setTextColor(ClubUi.text)
                    }
                }
            }
            adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
            tariffsView.adapter = adapter
            for (i in 0 until seats.length()) {
                val seat = seats.getJSONObject(i)
                val id = seat.getInt("seatId")
                val number = seat.getInt("seatNumber")
                val active = seat.getBoolean("isActive")
                seatsView.addView(Button(this).apply {
            ClubUi.button(this)
                    text = "ПК №$number" + if (active) " · выбрать" else " · отключён"
                    isEnabled = active
                    setOnClickListener {
                        seatId = id
                        selectedSeatText.text = "Выбран ПК №$number"
                        status.text = "Выбран ПК №$number. Свободное время проверит сервер."
                        showScreen("Бронирование")
                    }
                })
            }
            status.text = "Загружено ${seats.length()} мест и ${prices.length()} тарифов"
        }
    }

    private fun book() {
        val draft = BookingDraftValidator.parse(
            seatId, tariffs.getOrNull(tariffsView.selectedItemPosition),
            startUtc.text.toString(), endUtc.text.toString())
        if (draft == null) {
            status.text = "Выберите компьютер, тариф и корректное UTC-время до 7 суток (целые минуты)."
            return
        }
        execute({
            api.book(draft.seatId, draft.tariffId,
                draft.startUtc.toString(), draft.endUtc.toString())
        }) { result ->
            stopClockPolling()
            sessionRunning = false
            bookingId = result.getInt("bookingId")
            bookedEnd = draft.endUtc
            sessionView.text = "Бронь №$bookingId, цена ${result.getDouble("priceRub")} ₽"
            status.text = "Бронь успешно создана"
            showScreen("Сессия")
        }
    }

    private fun sessionAction(action: String) {
        val id = bookingId
        if (id == null) {
            status.text = "Сначала создайте бронирование."
            return
        }
        when (action) {
            "start" -> execute({ api.startSession(id) }) {
                status.text = "Сессия запущена"
                sessionRunning = true
                startClockPolling()
            }
            "clock" -> execute({ api.clock(id) }) { value ->
                sessionView.text = "Сессия №${value.getInt("sessionId")}\n" +
                    SessionClockFormatter.format(value.getInt("remainingSeconds"))
            }
            "extend" -> {
                val oldEnd = bookedEnd ?: return
                val newEnd = oldEnd.plus(30, ChronoUnit.MINUTES)
                execute({ api.extend(id, newEnd.toString()) }) { result ->
                    bookedEnd = runCatching {
                        Instant.parse(result.getString("newEndUtc"))
                    }.getOrDefault(newEnd)
                    status.text = "Продлено; доплата ${result.getDouble("extraChargeRub")} ₽"
                    sessionAction("clock")
                }
            }
        }
    }
}
