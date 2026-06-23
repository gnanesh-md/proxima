  const menuBtn = document.getElementById("menu-btn");
  const menuIcon = document.getElementById("menu-icon");
  const mobileMenu = document.getElementById("mobile-menu");

  menuBtn.addEventListener("click", () => {
    // Toggle mobile menu
    mobileMenu.classList.toggle("hidden");

    // Toggle icon between hamburger and X
    if (menuIcon.innerHTML === "☰") {
      menuIcon.innerHTML = "✖"; // X icon
    } else {
      menuIcon.innerHTML = "☰"; // Hamburger icon
    }
  });
