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
    private var authenticated = false
    private var userLogin = ""
    private var userRole = ""
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
                                sessionView.text = "Сессия №${value.getInt("sessionId")}, осталось $remaining сек."
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

    override fun onStart() {
        super.onStart()
        if (::sessionView.isInitialized && sessionRunning && bookingId != null) startClockPolling()
    }

    private fun label(value: String) = TextView(this).apply {
        text = value
        textSize = 18f
        setTextColor(Color.WHITE)
        setPadding(4, 12, 4, 12)
    }
    private fun input(value: String) = EditText(this).apply {
        setText(value)
        setTextColor(Color.WHITE)
        setSingleLine()
    }
    private fun <T> execute(task: () -> T, complete: (T) -> Unit) {
        Thread {
            try {
                val value = task()
                runOnUiThread { complete(value) }
            } catch (ex: Exception) {
                runOnUiThread { status.text = "Ошибка API: ${ex.message}" }
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
            setBackgroundColor(Color.rgb(18, 25, 40))
            setPadding(14, 14, 14, 14)
        }
        setContentView(outer)
        outer.addView(label("GamingClub · Вариант 18"))
        status = label("Войдите или зарегистрируйтесь")
        outer.addView(status)
        val nav = HorizontalScrollView(this)
        val navButtons = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        nav.addView(navButtons)
        outer.addView(nav)
        val scroll = ScrollView(this)
        outer.addView(scroll, LinearLayout.LayoutParams(-1, 0, 1f))
        val content = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        scroll.addView(content)
        val labels = listOf(
            "Вход", "Зал", "Бронирование", "Мои брони", "Сессия", "Профиль"
        )
        for (name in labels) {
            screens[name] = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
            content.addView(screens.getValue(name))
            navButtons.addView(Button(this).apply {
                text = name
                setOnClickListener {
                    if (!authenticated && name != "Вход") {
                        status.text = "Сначала войдите в аккаунт"
                        showScreen("Вход")
                    } else {
                        showScreen(name)
                        if (name == "Мои брони") loadMine()
                    }
                }
            })
        }
        val login = screens.getValue("Вход")
        login.addView(label("Войти / зарегистрироваться"))
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
            text = "Войти"
            setOnClickListener { authenticate(false) }
        })
        login.addView(Button(this).apply {
            text = "Создать аккаунт посетителя"
            setOnClickListener { authenticate(true) }
        })

        val hall = screens.getValue("Зал")
        hall.addView(label("1. Схема зала"))
        hall.addView(Button(this).apply {
            text = "Обновить зал"
            setOnClickListener { refresh() }
        })
        hallPlan = HallPlanView(this).apply {
            onSeatSelected = { id, number ->
                seatId = id
                status.text = "Выбран ПК №$number. Доступность проверит сервер."
                showScreen("Бронирование")
            }
        }
        hall.addView(hallPlan)
        hall.addView(label("Зелёный — активен, серый — отключён, жёлтый — выбран."))
        seatsView = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        hall.addView(seatsView)

        val booking = screens.getValue("Бронирование")
        booking.addView(label("2. Выбор тарифа и времени UTC"))
        tariffsView = Spinner(this)
        booking.addView(tariffsView)
        val start = Instant.now().truncatedTo(ChronoUnit.MINUTES).plus(2, ChronoUnit.MINUTES)
        booking.addView(label("Начало UTC, например 2026-11-20T14:00:00Z"))
        startUtc = input(start.toString())
        booking.addView(startUtc)
        booking.addView(label("Конец UTC"))
        endUtc = input(start.plus(1, ChronoUnit.HOURS).toString())
        booking.addView(endUtc)
        booking.addView(Button(this).apply {
            text = "Забронировать выбранный ПК"
            setOnClickListener { book() }
        })

        val mine = screens.getValue("Мои брони")
        mine.addView(label("3. Мои бронирования"))
        mine.addView(Button(this).apply {
            text = "Обновить список"
            setOnClickListener { loadMine() }
        })
        myBookings = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        mine.addView(myBookings)

        val session = screens.getValue("Сессия")
        session.addView(label("4. Текущая сессия"))
        sessionView = label("Выберите бронь или создайте новую")
        session.addView(sessionView)
        session.addView(Button(this).apply {
            text = "Запустить сессию"
            setOnClickListener { sessionAction("start") }
        })
        session.addView(Button(this).apply {
            text = "Обновить таймер"
            setOnClickListener { sessionAction("clock") }
        })
        session.addView(Button(this).apply {
            text = "Продлить на 30 минут"
            setOnClickListener { sessionAction("extend") }
        })

        val profile = screens.getValue("Профиль")
        profile.addView(label("5. Профиль"))
        profileText = label("Вы не вошли")
        profile.addView(profileText)
        profile.addView(Button(this).apply {
            text = "Выйти"
            setOnClickListener {
                stopClockPolling()
                api.clearToken()
                authenticated = false
                bookingId = null
                bookedEnd = null
                userLogin = ""
                userRole = ""
                profileText.text = "Вы не вошли"
                status.text = "Сессия входа завершена"
                showScreen("Вход")
            }
        })
        showScreen("Вход")
    }

    private fun showScreen(name: String) {
        screens.forEach { (key, view) ->
            view.visibility = if (key == name) android.view.View.VISIBLE else android.view.View.GONE
        }
    }

    private fun authenticate(register: Boolean) {
        val login = loginName.text.toString().trim()
        val password = loginPassword.text.toString()
        val name = displayName.text.toString().trim()
        if (login.isBlank() || password.isBlank() || (register && name.isBlank())) {
            status.text = "Заполните обязательные поля"
            return
        }
        execute({
            if (register) api.register(login, password, name) else api.login(login, password)
        }) { result ->
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
        execute({ api.mine() }) { rows ->
            myBookings.removeAllViews()
            for (i in 0 until rows.length()) {
                val booking = rows.getJSONObject(i)
                val id = booking.getInt("bookingId")
                val to = booking.getString("endUtc")
                val row = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
                row.addView(Button(this).apply {
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
                        text = "Отменить бронь №$id"
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
            hallPlan.submit(seats)
            tariffs.clear()
            val names = mutableListOf<String>()
            for (i in 0 until prices.length()) {
                val tariff = prices.getJSONObject(i)
                tariffs += tariff.getInt("tariffId")
                names += "${tariff.getString("name")} — ${tariff.getDouble("priceRub")} ₽"
            }
            tariffsView.adapter = ArrayAdapter(this,
                android.R.layout.simple_spinner_dropdown_item, names)
            for (i in 0 until seats.length()) {
                val seat = seats.getJSONObject(i)
                val id = seat.getInt("seatId")
                val number = seat.getInt("seatNumber")
                val active = seat.getBoolean("isActive")
                seatsView.addView(Button(this).apply {
                    text = "ПК №$number" + if (active) " · выбрать" else " · отключён"
                    isEnabled = active
                    setOnClickListener {
                        seatId = id
                        status.text = "Выбран ПК №$number. Свободное время проверит сервер."
                    }
                })
            }
            status.text = "Загружено ${seats.length()} мест и ${prices.length()} тарифов"
        }
    }

    private fun book() {
        val seat = seatId
        val tariff = tariffs.getOrNull(tariffsView.selectedItemPosition)
        val start = runCatching { Instant.parse(startUtc.text.toString()) }.getOrNull()
        val end = runCatching { Instant.parse(endUtc.text.toString()) }.getOrNull()
        if (seat == null || tariff == null ||
            start == null || end == null || end <= start ||
            start.truncatedTo(ChronoUnit.MINUTES) != start ||
            end.truncatedTo(ChronoUnit.MINUTES) != end) {
            status.text = "Выберите место, тариф и корректное UTC-время."
            return
        }
        execute({ api.book(seat, tariff, start.toString(), end.toString()) }) { result ->
            stopClockPolling()
            sessionRunning = false
            bookingId = result.getInt("bookingId")
            bookedEnd = end
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
                sessionView.text = "Сессия №${value.getInt("sessionId")}, осталось ${value.getInt("remainingSeconds")} сек."
            }
            "extend" -> {
                val oldEnd = bookedEnd ?: return
                val newEnd = oldEnd.plus(30, ChronoUnit.MINUTES)
                execute({ api.extend(id, newEnd.toString()) }) { result ->
                    bookedEnd = newEnd
                    status.text = "Продлено; доплата ${result.getDouble("extraChargeRub")} ₽"
                    sessionAction("clock")
                }
            }
        }
    }
}
